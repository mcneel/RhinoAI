namespace Rhino.AI;

internal sealed record LegacySettingsSnapshot(
    int? ZoomLevel,
    bool? AutoLoadMCP,
    bool? AutoLoadScriptPlugIn,
    string? ScriptPluginName,
    string? DefaultAgentName,
    IReadOnlyList<string> DisabledTools,
    IReadOnlyList<string> DisabledAgents,
    IReadOnlyDictionary<string, string> ModelsByAgentName,
    IReadOnlyDictionary<string, string> PromptsByAgentName,
    string? ExtraMcpServersJson);

internal sealed record MigrationStep(string Key, string LegacyValue, string AppliedTo);
