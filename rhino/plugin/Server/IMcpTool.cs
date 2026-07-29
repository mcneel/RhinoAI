using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server;

/// <summary>
/// Anything the dispatcher can list in <c>tools/list</c> and invoke from <c>tools/call</c>,
/// whether it was compiled into this assembly or contributed at run time by another Rhino
/// plug-in.
/// </summary>
/// <remarks>
/// Implemented by <c>ToolHandler</c> for compiled tools and by
/// <see cref="Extensibility.ProviderToolHandler"/> for contributed ones.
/// </remarks>
internal interface IMcpTool
{
    /// <summary>
    /// The name clients use in <c>tools/list</c> and <c>tools/call</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Optional human-readable title, surfaced as an annotation.
    /// </summary>
    string? Title { get; }

    /// <summary>
    /// What the tool does. Shown to the model, so it drives tool selection.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Advisory hint that the tool does not modify state.
    /// </summary>
    bool ReadOnly { get; }

    /// <summary>
    /// Advisory hint that the tool may make changes that are not easily undone.
    /// </summary>
    bool Destructive { get; }

    /// <summary>
    /// Whether the tool is hidden from the external "/" endpoint and offered only on the
    /// in-panel "/agent" endpoint.
    /// </summary>
    /// <remarks>
    /// Contributed tools are always false: a plug-in cannot currently register something that
    /// only the in-panel agent may call.
    /// </remarks>
    bool InPanelOnly { get; }

    /// <summary>
    /// The JSON Schema for this tool's arguments.
    /// </summary>
    JsonElement InputSchema { get; }

    /// <summary>
    /// Runs the tool.
    /// </summary>
    /// <param name="arguments">Raw argument values keyed by parameter name; may be null.</param>
    /// <param name="scope">The request's service scope.</param>
    /// <param name="ct">Cancels the invocation.</param>
    /// <returns>
    /// The result to return to the client. A tool that fails is expected to report it as a
    /// result with <c>IsError</c> set, rather than by throwing — a failed tool is data, not a
    /// transport error.
    /// </returns>
    Task<CallToolResult> InvokeAsync(
        IDictionary<string, JsonElement>? arguments, IServiceProvider scope, CancellationToken ct);
}
