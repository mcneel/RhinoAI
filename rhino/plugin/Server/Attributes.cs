namespace RhMcp.Server;

// In-house replacements for the ModelContextProtocol.* attribute set. Same
// names + same property shapes used by existing tool/resource files, so the
// 38 [McpServerTool]/[McpServerResource] sites in this plugin don't change.
// Only GlobalUsings.cs swaps which namespace the symbol resolves from.

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class McpServerToolTypeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class McpServerToolAttribute : Attribute
{
    public string? Name { get; set; }
    public string? Title { get; set; }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class McpServerResourceTypeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class McpServerResourceAttribute : Attribute
{
    public string? UriTemplate { get; set; }
    public string? Name { get; set; }
    public string? MimeType { get; set; }
}
