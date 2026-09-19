namespace Rhino.AI;

// One source of truth for the MCP-server entry every host hands its agent: launch the
// bundled rhino-mcp-router over stdio. The router then discovers this session's HTTP
// listener via the on-disk announcement (see RhinoMcpHost.WriteAnnouncement).
internal static class RouterMcpConfig
{
    // The MCP server id; also the tool-name prefix (mcp__rhino__*).
    internal const string ServerName = "rhino";

    // The {"mcpServers":...} shape Claude Code, the connector json, and the test harness accept.
    internal static string Json => BuildJson(null);

    // Same shape, with an optional env block (the Connect dialog's Advanced tab).
    // Empty/null env keeps the bare command-only entry.
    internal static string BuildJson(IReadOnlyDictionary<string, string>? env) =>
        JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object> { [ServerName] = Entry(env) },
        }, IndentedOptions);

    // Compact `"rhino": { ... }` fragment for the copy-paste prompt.
    internal static string EntryFragment(IReadOnlyDictionary<string, string>? env) =>
        $"\"{ServerName}\": {JsonSerializer.Serialize(Entry(env), CompactOptions)}";

    private static object Entry(IReadOnlyDictionary<string, string>? env) =>
        env is { Count: > 0 }
            ? new { command = RouterPath, env }
            : new { command = RouterPath };

    internal static string RouterPath => RouterStaging.EnsureStaged().RouterPath;

    // Indented for the connector dialog's readability; harmless as a single argv string.
    private static JsonSerializerOptions IndentedOptions { get; } =
        new(McpSerializer.Options) { WriteIndented = true };

    // Compact form for embedding a one-line entry inside the prompt text.
    private static JsonSerializerOptions CompactOptions { get; } =
        new(McpSerializer.Options) { WriteIndented = false };

}
