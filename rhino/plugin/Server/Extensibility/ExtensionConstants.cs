using System.Text.RegularExpressions;

namespace RhMcp.Server.Extensibility;

/// <summary>
/// Constants for the contract other Rhino plug-ins use to contribute MCP
/// tools to this server. See <see cref="McpExtensionHost"/> for the entry point, and the
/// "Extending the tool set from another Rhino plug-in" section of <c>rhino/README.md</c>
/// for the provider-facing description.
/// </summary>
internal static class ExtensionConstants
{
    /// <summary>
    /// The contract version, surfaced to callers as
    /// <see cref="McpExtensionHost.McpExtensionProtocol"/>.
    /// </summary>
    /// <remarks>
    /// Bumped only for a breaking change to <see cref="McpExtensionHost"/>'s members or to
    /// the descriptor shape. Additive changes -- a new optional descriptor field, a new
    /// method -- leave it alone, since an older caller keeps working against them.
    /// </remarks>
    public const int Version = 1;

    /// <summary>
    /// Tool-name prefix reserved for this server's own tools. A contributing plug-in may
    /// not register a name beginning <c>ext_</c>.
    /// </summary>
    /// <remarks>
    /// Reserving it keeps a namespace in which the host can add tools later without
    /// colliding with anyone, and stops a contributed tool impersonating a built-in one.
    /// </remarks>
    public const string ReservedToolPrefix = "ext";

 
    /// <summary>
    /// Deliberately narrow: MCP clients and the router's generated proxies both key on
    /// tool names, so anything exotic risks being mangled somewhere downstream rather
    /// than rejected here, where the plug-in author can still see why.
    /// </summary>
    public static readonly Regex ToolNamePattern =
        new("^[A-Za-z][A-Za-z0-9_.-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
