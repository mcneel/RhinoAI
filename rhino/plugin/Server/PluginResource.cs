namespace RhMcp.Server;

// A resource discovered on disk under a Rhino plug-in's `mcp/` folder.
// Distinct from ResourceHandler (which wraps a C# method) because the read
// path is "open a file" rather than "reflect-and-invoke". Held in a separate
// list on ResourceRegistry; the dispatcher tries reflection-based handlers
// first, then falls through to plug-in resources.
//
// Either FilePath is set (read content from disk) or IsIndex is true
// (synthetic catalog page, pre-rendered into IndexBody at scan time).
internal sealed class PluginResource
{
    public required string Uri { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string MimeType { get; init; } = "text/markdown";

    public string? FilePath { get; init; }

    public bool IsIndex { get; init; }
    public string? IndexBody { get; init; }
}
