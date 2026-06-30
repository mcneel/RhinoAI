namespace RhMcp;

/// <summary>
/// All MCP Toolsare forced on the UI Thread, this attribute excludes a tool from that
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class BackgroundThreadAttribute : Attribute { }
