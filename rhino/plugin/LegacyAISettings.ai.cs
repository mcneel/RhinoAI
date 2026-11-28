using Rhino.PlugIns;

namespace Rhino.AI;

internal static class LegacyAISettings
{

    private static Guid PluginId { get; } = new("2668d7ed-f507-4a68-8295-8172147a0e39");

    private static PersistentSettings Settings =>
        PlugIn.Find(PluginId) is PlugIn plugin
            ? plugin.Settings
            : throw new InvalidOperationException("RhinoAI plugin is not loaded; LegacyAISettings is unavailable.");

    private static string[] LegacyAgentNames { get; } = ["claude", "codex", "gemini"];

    private static string[] LegacyAgentFields { get; } = ["Model", "Prompt"];

    private static string[] LegacyKeys { get; } =
    [
        "ZoomLevel",
        "AutoLoadMCP",
        "AutoLoadScriptPlugIn",
        "ScriptPluginName",
        "DefaultAgentName",
        "DisabledTools",
        "DisabledAgents",
        "ExtraMcpServersJson",
        "PluginPermissions",
    ];

    private static JsonSerializerOptions TrustOptions { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static LegacySettingsSnapshot Read() => new(
        ZoomLevel: Integer("ZoomLevel"),
        AutoLoadMCP: Bool("AutoLoadMCP"),
        AutoLoadScriptPlugIn: Bool("AutoLoadScriptPlugIn"),
        ScriptPluginName: Text("ScriptPluginName"),
        DefaultAgentName: Text("DefaultAgentName"),
        DisabledTools: List("DisabledTools"),
        DisabledAgents: List("DisabledAgents"),
        ModelsByAgentName: PerAgent("Model"),
        PromptsByAgentName: PerAgent("Prompt"),
        ExtraMcpServersJson: Text("ExtraMcpServersJson"),
        PlugInTrust: PlugInTrust());

    // Conversations stays: ConversationStore still keeps live conversations under it.
    public static void Delete()
    {
        foreach (string key in LegacyKeys)
            Settings.DeleteItem(key);

        foreach (string name in LegacyAgentNames)
        {
            foreach (string field in LegacyAgentFields)
                Settings.DeleteItem(AgentKey(name, field));
        }
    }

    public static PersistentSettings Conversations =>
        Settings.TryGetChild(nameof(Conversations), out PersistentSettings child)
            ? child
            : Settings.AddChild(nameof(Conversations));

    // TryGet only: a Get with a default registers that default, after which an absent key reads as present.
    private static int? Integer(string key) => Settings.TryGetInteger(key, out int value) ? value : null;

    private static bool? Bool(string key) => Settings.TryGetBool(key, out bool value) ? value : null;

    private static string? Text(string key)
        => Settings.TryGetString(key, out string value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static IReadOnlyList<string> List(string key)
        => Settings.TryGetStringList(key, out string[] values) && values is not null ? [.. values] : [];

    private static IReadOnlyDictionary<string, string> PerAgent(string field)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in LegacyAgentNames)
        {
            if (Text(AgentKey(name, field)) is string value)
                values[name] = value;
        }

        return values;
    }

    private static string AgentKey(string name, string field) => string.Format("Agent_{0}_{1}", name, field);

    private static IReadOnlyList<PlugInPermissionEntry> PlugInTrust()
    {
        if (Text("PluginPermissions") is not string json)
            return [];

        try
        {
            PlugInPermissionEntry[] entries = JsonSerializer.Deserialize<PlugInPermissionEntry[]>(json, TrustOptions) ?? [];
            return entries.Where(entry => entry.Id != Guid.Empty && entry.Name is not null).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

}
