using System.Collections.Generic;
using System.Text.Json;
using Rhino.PlugIns;

namespace RhMcp;

internal static class AISettings
{

    private static Guid PluginId { get; } = new("2668d7ed-f507-4a68-8295-8172147a0e39");

    private static PersistentSettings Settings =>
        PlugIn.Find(PluginId) is PlugIn plugin
            ? plugin.Settings
            : throw new InvalidOperationException("RhMcp plugin is not loaded; AISettings is unavailable.");

    // Name of the agent the registry prefers when resolving the active one.
    public static string DefaultAgentName
    {
        get => Settings.GetString(nameof(DefaultAgentName), "claude");
        set => Settings.SetString(nameof(DefaultAgentName), value);
    }

    // Tools hidden from in-Rhino agents (Part 3). Empty = nothing hidden.
    public static string[] DisabledTools
    {
        get => Settings.GetStringList(nameof(DisabledTools), []);
        set => Settings.SetStringList(nameof(DisabledTools), value);
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

    // The full agent chain: built-ins (always present, Claude-first) overlaid with any custom
    // entries, in chain order. Custom entries that alias a built-in name override the built-in
    // in place; never duplicated. Built-ins are re-seeded on every read so they can't be lost.
    public static IReadOnlyList<AgentDefinition> GetAgents() =>
        AgentRegistry.Overlay(AgentRegistry.Builtins(), DeserializeAgents());

    // Persists the chain. Built-ins are not stored verbatim (they re-seed on read); we store
    // every entry's settable state so a built-in override (e.g. edited search paths) survives.
    public static void SetAgents(IReadOnlyList<AgentDefinition> agents) =>
        Settings.SetString(AgentsKey, JsonSerializer.Serialize(agents, McpSerializer.Options));

    private const string AgentsKey = "Agents";

    private static IReadOnlyList<AgentDefinition> DeserializeAgents()
    {
        string json = Settings.GetString(AgentsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            AgentDefinition[]? parsed = JsonSerializer.Deserialize<AgentDefinition[]>(json, McpSerializer.Options);
            return parsed ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Model identifiers the user has typed into the Model dropdown, remembered per adapter so they
    // reappear as choices. Kept separate from KnownModels (the built-in seeds) and merged on read.
    public static IReadOnlyList<string> GetCustomModels(AgentAdapter adapter) =>
        Settings.GetStringList(CustomModelsKey(adapter), []);

    // Remembers a user-typed model for its adapter. No-ops for blanks, built-in seeds, and
    // already-remembered values so the stored list stays a clean set of genuinely custom entries.
    public static void RememberCustomModel(AgentAdapter adapter, string model)
    {
        string trimmed = model.Trim();
        if (trimmed.Length == 0 || KnownModels.For(adapter).Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return;

        string[] existing = Settings.GetStringList(CustomModelsKey(adapter), []);
        if (existing.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return;

        string[] updated = [.. existing, trimmed];
        Settings.SetStringList(CustomModelsKey(adapter), updated);
    }

    private static string CustomModelsKey(AgentAdapter adapter) => $"CustomModels_{adapter}";

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
