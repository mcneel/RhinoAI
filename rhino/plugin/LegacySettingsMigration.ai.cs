using System.Globalization;

using Rhino.Runtime;

using Rhino.AI.PlugIns;
using Rhino.AI.UI.Settings;

namespace Rhino.AI;

internal static class LegacySettingsMigration
{

    private const string CustomMcpsKey = "ExtraMcpServersJson";

    private const string WithheldJson = "(not logged, may hold secrets)";

    public static IReadOnlyList<MigrationStep> Run()
    {
        IReadOnlyList<MigrationStep> steps = Apply(LegacyAISettings.Read(), AIInstances.Preferences, AIInstances.Agents);
        foreach (MigrationStep step in steps)
        {
            HostUtils.LogDebugEvent(string.Format("[rhino-ai] Legacy setting {0} = {1}: {2}\n", step.Key, step.LegacyValue, step.AppliedTo));
        }

        return steps;
    }

    public static IReadOnlyList<MigrationStep> Apply(LegacySettingsSnapshot legacy, UserPreferences preferences, Agents agents)
    {
        List<MigrationStep> steps = [];

        if (legacy.ZoomLevel is int zoomLevel)
        {
            preferences.ZoomLevel = zoomLevel;
            steps.Add(new MigrationStep("ZoomLevel", zoomLevel.ToString(CultureInfo.InvariantCulture), "Preferences.ZoomLevel"));
        }

        if (legacy.AutoLoadMCP is bool autoLoadMcp)
        {
            preferences.AutoLoadMCP = autoLoadMcp;
            steps.Add(new MigrationStep("AutoLoadMCP", Text(autoLoadMcp), "Preferences.AutoLoadMCP"));
        }

        if (legacy.AutoLoadScriptPlugIn is bool autoLoadScriptPlugIn)
        {
            preferences.AutoLoadScriptPlugIn = autoLoadScriptPlugIn;
            steps.Add(new MigrationStep("AutoLoadScriptPlugIn", Text(autoLoadScriptPlugIn), "Preferences.AutoLoadScriptPlugIn"));
        }

        if (legacy.ScriptPluginName is string scriptPluginName)
        {
            preferences.ScriptPluginName = scriptPluginName;
            steps.Add(new MigrationStep("ScriptPluginName", scriptPluginName, "Preferences.ScriptPluginName"));
        }

        if (legacy.DefaultAgentName is string defaultAgentName)
            steps.Add(PreferAgent(defaultAgentName, preferences, agents));

        foreach (string tool in legacy.DisabledTools)
            steps.Add(DenyTool(tool, preferences));

        foreach (string agentName in legacy.DisabledAgents)
            steps.Add(DisableAgent(agentName, agents));

        foreach (KeyValuePair<string, string> model in legacy.ModelsByAgentName)
            steps.Add(SetDefaultModel(model.Key, model.Value, agents));

        foreach (KeyValuePair<string, string> prompt in legacy.PromptsByAgentName)
            steps.Add(SetPrompt(prompt.Key, prompt.Value, agents));

        if (legacy.ExtraMcpServersJson is string servers)
            steps.AddRange(AddCustomMcps(servers, preferences));

        PlugInRegistry.ApplySavedPermissions(legacy.PlugInTrust);
        foreach (PlugInPermissionEntry entry in legacy.PlugInTrust)
            steps.Add(new MigrationStep("PluginPermissions", string.Format("{0} {1}", entry.Name, Text(entry.Enabled)), "PlugInRegistry trust"));

        return steps;
    }

    private static MigrationStep PreferAgent(string agentName, UserPreferences preferences, Agents agents)
    {
        if (CliAgentFor(agentName, agents) is not AgentDefinition agent)
            return Skipped("DefaultAgentName", agentName, "no matching agent");

        preferences.PreferedAgentId = agent.Id;
        return new MigrationStep("DefaultAgentName", agentName, string.Format("Preferences.PreferedAgentId ({0})", agent.DisplayName));
    }

    private static MigrationStep DenyTool(string tool, UserPreferences preferences)
    {
        if (string.IsNullOrWhiteSpace(tool))
            return Skipped("DisabledTools", tool, "blank tool name");

        string toolName = tool.Trim();
        preferences.Permissions.Replace(new Permission(ToolCatalogReader.BuiltinServerName, toolName, Permissability.Deny, []));
        return new MigrationStep("DisabledTools", toolName, string.Format("Preferences.Permissions ({0} {1} Deny)", ToolCatalogReader.BuiltinServerName, toolName));
    }

    private static MigrationStep DisableAgent(string agentName, Agents agents)
    {
        if (CliAgentFor(agentName, agents) is not AgentDefinition agent)
            return Skipped("DisabledAgents", agentName, "no matching agent");

        agent.Enabled = false;
        return new MigrationStep("DisabledAgents", agentName, string.Format("{0}.Enabled", agent.DisplayName));
    }

    private static MigrationStep SetDefaultModel(string agentName, string modelId, Agents agents)
    {
        string key = string.Format("Agent_{0}_Model", agentName);
        if (CliAgentFor(agentName, agents) is not AgentDefinition agent)
            return Skipped(key, modelId, "no matching agent");

        if (agent.AvailableModels.FirstOrDefault(model => string.Equals(model.ModelId, modelId.Trim(), StringComparison.OrdinalIgnoreCase)) is not ModelDefinition known)
            return Skipped(key, modelId, string.Format("{0} has no such model", agent.DisplayName));

        agent.DefaultModelId = known.ModelId;
        return new MigrationStep(key, modelId, string.Format("{0}.DefaultModelId", agent.DisplayName));
    }

    private static MigrationStep SetPrompt(string agentName, string prompt, Agents agents)
    {
        string key = string.Format("Agent_{0}_Prompt", agentName);
        if (CliAgentFor(agentName, agents) is not AgentDefinition agent)
            return Skipped(key, prompt, "no matching agent");

        agent.DefaultPrompt = prompt;
        return new MigrationStep(key, prompt, string.Format("{0}.DefaultPrompt", agent.DisplayName));
    }

    private static IReadOnlyList<MigrationStep> AddCustomMcps(string json, UserPreferences preferences)
    {
        switch (McpServerEntry.Read(json))
        {
            case ServerResult<IReadOnlyList<NamedServer>>.Ok ok:
                List<MigrationStep> steps = new(ok.Value.Count);
                foreach (NamedServer server in ok.Value)
                    steps.Add(AddCustomMcp(server, preferences));
                return steps;

            case ServerResult<IReadOnlyList<NamedServer>>.Failed { Problem: ServerProblem.NoServers }:
                return [new MigrationStep(CustomMcpsKey, WithheldJson, "no servers to add")];

            case ServerResult<IReadOnlyList<NamedServer>>.Failed failed:
                return [Skipped(CustomMcpsKey, WithheldJson, failed.Problem.ToString())];

            default:
                throw new InvalidOperationException("Unknown server result.");
        }
    }

    private static MigrationStep AddCustomMcp(NamedServer server, UserPreferences preferences)
    {
        if (string.Equals(server.Name, ToolCatalogReader.BuiltinServerName, StringComparison.OrdinalIgnoreCase))
            return Skipped(CustomMcpsKey, server.Name, "the built-in server has this name");

        if (preferences.CustomMcps.Any(existing => string.Equals(existing.Name, server.Name, StringComparison.OrdinalIgnoreCase)))
            return Skipped(CustomMcpsKey, server.Name, "a custom MCP already has this name");

        switch (McpServerEntry.ToDefinition(server, true))
        {
            case ServerResult<IMcpDefinition>.Failed failed:
                return Skipped(CustomMcpsKey, server.Name, failed.Problem.ToString());

            case ServerResult<IMcpDefinition>.Ok ok:
                preferences.CustomMcps.Add(ok.Value);
                IReadOnlyList<string> dropped = McpServerEntry.UnusedKeys(server, ok.Value);
                string appliedTo = string.Format("Preferences.CustomMcps ({0})", ok.Value.GetType().Name);
                return new MigrationStep(CustomMcpsKey, server.Name, dropped.Count == 0 ? appliedTo : string.Format("{0}, dropped {1}", appliedTo, string.Join(", ", dropped)));

            default:
                throw new InvalidOperationException("Unknown server result.");
        }
    }

    private static AgentDefinition? CliAgentFor(string agentName, Agents agents) => agentName.Trim().ToLowerInvariant() switch
    {
        "claude" => CliAgentOf("Anthropic", agents),
        "codex" => CliAgentOf("OpenAI", agents),
        _ => null,
    };

    // Not Agents.FromParameters: that skips disabled agents, and DisableAgent runs before the model and prompt steps.
    private static AgentDefinition? CliAgentOf(string vendor, Agents agents)
        => agents.Definitions.OfType<CliAgentDefinition>().FirstOrDefault(definition => string.Equals(definition.Vendor, vendor, StringComparison.OrdinalIgnoreCase));

    private static MigrationStep Skipped(string key, string legacyValue, string reason)
        => new(key, legacyValue, string.Format("skipped, {0}", reason));

    private static string Text(bool value) => value ? "true" : "false";

}
