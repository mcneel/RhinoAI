using System.Collections.Generic;
using System.Text.Json;
using Rhino.PlugIns;

namespace Rhino.AI;

internal static class AISettings
{

    private static Guid PluginId { get; } = new("2668d7ed-f507-4a68-8295-8172147a0e39");

    private static PersistentSettings Settings =>
        PlugIn.Find(PluginId) is PlugIn plugin
            ? plugin.Settings
            : throw new InvalidOperationException("RhinoAI plugin is not loaded; AISettings is unavailable.");

    // The one setting that is per assistant is its tool permissions, and this is its key. Rhino's
    // prefix is empty, so the keys that predate profiles are the ones it still reads.
    private static string Key(AIProfile profile, string name) => AIProfiles.SettingsPrefix(profile) + name;

    // Which agent, which model, which prompt and which agents exist at all are ONE set of settings
    // for every assistant: the choice of agent is about the machine it runs on, not about which
    // panel is asking. They keep the unprefixed keys, so what the AI panel had is what they all get.
    public static string DefaultAgentName() => Settings.GetString("DefaultAgentName", "claude");

    public static void SetDefaultAgentName(string value) => Settings.SetString("DefaultAgentName", value);

    // Tools hidden from in-Rhino agents (Part 3). Empty = nothing hidden. Superseded by
    // ToolModeOverrides; still read as Off for settings written before modes existed.
    public static string[] DisabledTools
    {
        get => Settings.GetStringList(nameof(DisabledTools), []);
        set => Settings.SetStringList(nameof(DisabledTools), value);
    }

    // Per-tool modes for a profile's agent as "name=mode" (off | on | ask), only where the user
    // departed from the profile's default for the tool.
    public static string[] ToolModeOverrides(AIProfile profile) => Settings.GetStringList(Key(profile, "ToolModeOverrides"), []);

    private static void SetToolModeOverrides(AIProfile profile, string[] value) => Settings.SetStringList(Key(profile, "ToolModeOverrides"), value);

    public static ToolMode ToolModeFor(AIProfile profile, string name, ToolMode @default)
    {
        foreach (string entry in ToolModeOverrides(profile))
            if (ToolModes.IsFor(entry, name) && ToolModes.TryParseEntry(entry, out _, out ToolMode mode))
                return mode;
        return profile == AIProfile.Rhino && DisabledTools.Contains(name, StringComparer.OrdinalIgnoreCase) ? ToolMode.Off : @default;
    }

    // Back to whatever the defaults currently say. Worth having as one action: an override is only
    // stored where the user departed from the default OF THE DAY, so settings saved before a default
    // changed keep winning over the new one, and the only way to see the new default is to drop them.
    public static void ClearToolModeOverrides(AIProfile profile)
    {
        SetToolModeOverrides(profile, []);
        if (profile == AIProfile.Rhino)
            DisabledTools = [];
    }

    public static void SetToolMode(AIProfile profile, string name, ToolMode mode, ToolMode @default)
    {
        string[] others = ToolModeOverrides(profile).Where(entry => !ToolModes.IsFor(entry, name)).ToArray();
        SetToolModeOverrides(profile, mode == @default ? others : [.. others, ToolModes.FormatEntry(name, mode)]);
        if (profile == AIProfile.Rhino)
            DisabledTools = DisabledTools
                .Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    // Definitions ship read-only in Definitions.json, so user edits are stored here and layered on at use.
    public static string[] DisabledAgents() => Settings.GetStringList("DisabledAgents", []);

    private static void SetDisabledAgents(string[] value) => Settings.SetStringList("DisabledAgents", value);

    // The panel's text zoom as the percentage the user sees; the ladder itself lives in the panel.
    public static int ZoomLevel
    {
        get => Settings.GetInteger(nameof(ZoomLevel), 100);
        set => Settings.SetInteger(nameof(ZoomLevel), value);
    }

    public static bool IsEnabled(AgentDefinition def) =>
        def.Enabled && !DisabledAgents().Contains(def.Name, StringComparer.OrdinalIgnoreCase);

    public static void SetAgentEnabled(string name, bool enabled)
    {
        string[] remaining = DisabledAgents()
            .Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        SetDisabledAgents(enabled ? remaining : [.. remaining, name]);
    }

    public static string AgentModel(string name) => Settings.GetString(AgentKey(name, "Model"), string.Empty);

    public static void SetAgentModel(string name, string model) => Settings.SetString(AgentKey(name, "Model"), model);

    public static string AgentPrompt(string name) => Settings.GetString(AgentKey(name, "Prompt"), string.Empty);

    public static void SetAgentPrompt(string name, string prompt) => Settings.SetString(AgentKey(name, "Prompt"), prompt);

    public static string EffectiveModel(AgentDefinition def) =>
        AgentModel(def.Name) is { Length: > 0 } model ? model : def.DefaultModel;

    // The user's own prompt, shared like the rest. What makes an assistant itself is the steer
    // AgentPrompts adds per profile on top of this, which is ours and not a setting.
    public static string EffectivePrompt(AgentDefinition def) =>
        AgentPrompt(def.Name) is { Length: > 0 } prompt ? prompt : def.DefaultPrompt;

    private static string AgentKey(string name, string field) => $"Agent_{name.ToLowerInvariant()}_{field}";

    // Overrides the username-derived name of the plugin manage_plugin_commands writes to.
    public static string? ScriptPluginName
    {
        get
        {
            string stored = Settings.GetString(nameof(ScriptPluginName), string.Empty);
            return string.IsNullOrWhiteSpace(stored) ? null : stored;
        }
        set => Settings.SetString(nameof(ScriptPluginName), value ?? string.Empty);
    }

    // External MCP servers merged into each agent's own config beside `rhino` (Part 4).
    public static string ExtraMcpServersJson
    {
        get => Settings.GetString(nameof(ExtraMcpServersJson), "{\"mcpServers\":{}}");
        set => Settings.SetString(nameof(ExtraMcpServersJson), value);
    }

    // Per-session conversation history lives under its own child node (Part 6).
    public static PersistentSettings Conversations =>
        Settings.TryGetChild(nameof(Conversations), out PersistentSettings child)
            ? child
            : Settings.AddChild(nameof(Conversations));

    public static int StartingPort
    {
        get => Settings.GetInteger(nameof(StartingPort), 10500);
        set => Settings.SetInteger(nameof(StartingPort), value);
    }

    public const int MinPort = 1;
    public const int MaxPort = 65535;


    public static string AcpCommand
    {
        get => Settings.GetString(nameof(AcpCommand), "npx");
        set => Settings.SetString(nameof(AcpCommand), value);
    }

    public static string AcpPackage
    {
        get => Settings.GetString(nameof(AcpPackage), "@agentclientprotocol/claude-agent-acp");
        set => Settings.SetString(nameof(AcpPackage), value);
    }

    public static string ClaudeCommandFileName
    {
        get => Settings.GetString(nameof(ClaudeCommandFileName), "claude");
        set => Settings.SetString(nameof(ClaudeCommandFileName), value);
    }

    public static string CodexCommandFileName
    {
        get => Settings.GetString(nameof(CodexCommandFileName), "codex");
        set => Settings.SetString(nameof(CodexCommandFileName), value);
    }

    public static TimeSpan AgentStartupTimeout
    {
        get => TimeSpan.FromMilliseconds(Settings.GetInteger(nameof(AgentStartupTimeout), 60_000));
        set => Settings.SetInteger(nameof(AgentStartupTimeout), (int)value.TotalMilliseconds);
    }


    public static TimeSpan ScriptCleanupDelay
    {
        get => TimeSpan.FromMilliseconds(Settings.GetInteger(nameof(ScriptCleanupDelay), 15_000));
        set => Settings.SetInteger(nameof(ScriptCleanupDelay), (int)value.TotalMilliseconds);
    }

    public static TimeSpan CommandHelpHttpTimeout
    {
        get => TimeSpan.FromMilliseconds(Settings.GetInteger(nameof(CommandHelpHttpTimeout), 2_000));
        set => Settings.SetInteger(nameof(CommandHelpHttpTimeout), (int)value.TotalMilliseconds);
    }

    public static TimeSpan SlotCloseTempDeleteDelay
    {
        get => TimeSpan.FromMilliseconds(Settings.GetInteger(nameof(SlotCloseTempDeleteDelay), 1_000));
        set => Settings.SetInteger(nameof(SlotCloseTempDeleteDelay), (int)value.TotalMilliseconds);
    }

    public static TimeSpan RouterExitDelay
    {
        get => TimeSpan.FromMilliseconds(Settings.GetInteger(nameof(RouterExitDelay), 200));
        set => Settings.SetInteger(nameof(RouterExitDelay), (int)value.TotalMilliseconds);
    }

    public static int MaxCommandResults
    {
        get => Settings.GetInteger(nameof(MaxCommandResults), 200);
        set => Settings.SetInteger(nameof(MaxCommandResults), value);
    }

    public static int DefaultObjectListLimit
    {
        get => Settings.GetInteger(nameof(DefaultObjectListLimit), 1000);
        set => Settings.SetInteger(nameof(DefaultObjectListLimit), value);
    }

    public static int DefaultViewportImageWidth
    {
        get => Settings.GetInteger(nameof(DefaultViewportImageWidth), 480);
        set => Settings.SetInteger(nameof(DefaultViewportImageWidth), value);
    }

    public static int DefaultViewportImageHeight
    {
        get => Settings.GetInteger(nameof(DefaultViewportImageHeight), 270);
        set => Settings.SetInteger(nameof(DefaultViewportImageHeight), value);
    }

    public const int MaxViewportImageWidth = 1280;
    public const int MaxViewportImageHeight = 720;
}
