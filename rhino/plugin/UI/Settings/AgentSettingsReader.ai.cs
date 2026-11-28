using System.IO;

using Rhino.AI.Secrets;

namespace Rhino.AI.UI.Settings;

internal static class AgentSettingsReader
{
    private const string DefinitionsResource = "Rhino.AI.Definitions.json";

    public static IReadOnlyList<AgentDefinition> BuiltinDefinitions()
    {
        using Stream stream = typeof(AIInstances).Assembly.GetManifestResourceStream(DefinitionsResource)
            ?? throw new InvalidOperationException($"The agent definitions resource '{DefinitionsResource}' is missing from the AI SDK.");
        using StreamReader reader = new(stream);
        return AgentDefinitionsJson.Parse(reader.ReadToEnd())
            ?? throw new InvalidOperationException($"The agent definitions resource '{DefinitionsResource}' could not be read.");
    }

    public static IReadOnlyList<AgentSettings> Current(IReadOnlyList<AgentDefinition> builtin)
    {
        Dictionary<Guid, int> builtinOrder = new(builtin.Count);
        for (int i = 0; i < builtin.Count; i++)
            builtinOrder[builtin[i].Id] = i;

        return AIInstances.Agents.Definitions
            .OrderBy(d => builtinOrder.TryGetValue(d.Id, out int position) ? position : int.MaxValue)
            .ThenBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => From(d, builtinOrder.ContainsKey(d.Id)))
            .OfType<AgentSettings>()
            .ToList();
    }

    public static IReadOnlyList<AgentSettings> Templates(IReadOnlyList<AgentDefinition> builtin) =>
        builtin.Select(d => From(d, isBuiltin: true)).OfType<AgentSettings>().ToList();

    public static KeySource KeySourceOf(string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            return KeySource.None;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(keyName)))
            return KeySource.Environment;

        return Stasher.TryGetSecret(keyName, out string? stored) && stored.Length > 0 ? KeySource.Vault : KeySource.None;
    }

    private static AgentSettings? From(IAgentDefinition definition, bool isBuiltin) => definition switch
    {
        CliAgentDefinition cli => Settings(cli, new CliConnection([.. cli.WinSearchPaths], [.. cli.MacSearchPaths]), isBuiltin),
        HttpAgentDefinition { ModelsRoute: string route } local =>
            Settings(local, new LocalConnection(local.Host ?? string.Empty, local.Protocol, route), isBuiltin),
        HttpAgentDefinition api =>
            Settings(api, new ApiConnection(api.Host ?? string.Empty, api.Protocol, api.ApiKeyEnvVar, KeySourceOf(api.ApiKeyEnvVar)), isBuiltin),
        _ => null,
    };

    private static AgentSettings Settings(AgentDefinition definition, AgentConnection connection, bool isBuiltin) => new(
        Id: definition.Id.ToString(),
        Name: definition.DisplayName,
        Vendor: definition.Vendor,
        Enabled: definition.Enabled,
        DefaultModelId: string.IsNullOrWhiteSpace(definition.DefaultModelId) ? null : definition.DefaultModelId,
        Models: definition.AvailableModels.Select(m => new ModelEntry(m.ModelId, m.DisplayName, OutputTokenLimit.Format(m.MaxOutputTokens))).ToList(),
        Prompt: definition.DefaultPrompt,
        Connection: connection,
        Builtin: isBuiltin,
        Platforms: PlatformsOf(definition.Platforms));

    private static IReadOnlyList<HostOs> PlatformsOf(Platform platforms)
    {
        List<HostOs> list = new(2);
        if ((platforms & Platform.Windows) != 0)
            list.Add(HostOs.Windows);
        if ((platforms & Platform.OSX) != 0)
            list.Add(HostOs.Mac);
        return list;
    }
}
