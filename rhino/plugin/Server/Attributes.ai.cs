namespace Rhino.AI.Server;

// In-house replacements for the ModelContextProtocol.* attribute set. Same
// names + same property shapes used by existing tool/resource files, so the
// 38 [McpServerTool]/[McpServerResource] sites in this plugin don't change.
// Only GlobalUsings.cs swaps which namespace the symbol resolves from.

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class McpServerToolTypeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class McpServerToolAttribute(
    string name, string? title = null, bool readOnly = false, bool destructive = false,
    bool enabledByDefault = true, bool confirmByDefault = false) : Attribute
{
    public string? Name { get; } = name;
    public string? Title { get; } = title;

    // Below is required for the Claude Connector

    public bool ReadOnly { get; } = readOnly;
    public bool Destructive { get; } = destructive;

    // The mode the in-Rhino agent starts with (Off / On / Ask); the user overrides it in AI Settings.
    public bool EnabledByDefault { get; } = enabledByDefault;
    public bool ConfirmByDefault { get; } = confirmByDefault;
}

// Groups a tool type's tools under one label in AI Settings and in the panel's quick switches.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ToolGroupAttribute(string label) : Attribute
{
    public string Label { get; } = label;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class McpServerResourceTypeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class McpServerResourceAttribute : Attribute
{
    public string? UriTemplate { get; set; }
    public string? Name { get; set; }
    public string? MimeType { get; set; }
}
