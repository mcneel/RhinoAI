using System.Threading.Tasks;

namespace RhMcp.Server;

// The dispatcher's half of the extensibility surface, kept out of McpEndpoint.cs so that
// upstream file differs from McNeel's only by the three lines that call into here.
internal sealed partial class McpDispatcher
{
    /// <summary>
    /// Every tool this endpoint can offer: the compiled ones first, then those contributed at
    /// run time by other Rhino plug-ins.
    /// </summary>
    /// <returns>
    /// A lazily evaluated sequence. Compiled tools keep <c>ToolRegistry</c>'s order; contributed
    /// ones follow in registry order, with any whose name a compiled tool already uses filtered
    /// out.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Compiled names win on a collision, matching
    /// <see cref="TryResolveTool"/> — so a tool cannot appear in <c>tools/list</c> under a name
    /// that <c>tools/call</c> would then route somewhere else. Listing and dispatch must agree
    /// on precedence or a contributed tool becomes visible but uncallable.
    /// </para>
    /// <para>
    /// The registry is read live rather than cached, so a plug-in that registers after this
    /// server started shows up on the next <c>tools/list</c> with no restart. That matters
    /// because plug-in load order is not something a contributor controls.
    /// </para>
    /// <para>
    /// Deferred execution means the collision filter runs at enumeration time, so callers should
    /// enumerate once and materialise if they need the set twice.
    /// </para>
    /// </remarks>
    private IEnumerable<IMcpTool> AllTools() =>
        _tools.All.Cast<IMcpTool>().Concat(
            Extensibility.McpExtensionRegistry.Current.Tools.Where(t => !_tools.TryGet(t.Name, out _)));

    /// <summary>
    /// Finds the handler for a tool name, looking first among the compiled tools and then
    /// among those contributed at run time by other Rhino plug-ins.
    /// </summary>
    /// <param name="name">
    /// The tool name exactly as the client sent it. Compiled tools match by whatever rule
    /// <c>ToolRegistry</c> applies; contributed ones match case-insensitively.
    /// </param>
    /// <param name="tool">
    /// The resolved handler, or null when the name is unknown. Only meaningful when this
    /// method returns true.
    /// </param>
    /// <returns>True when a tool of that name exists in either source.</returns>
    /// <remarks>
    /// <para>
    /// Order matters: compiled tools win over runtime-contributed ones on a name collision.
    /// A contributing plug-in therefore cannot shadow a built-in tool, whether by accident or
    /// deliberately — it can only add names the host does not already use. The reserved
    /// <see cref="Extensibility.ExtensionProtocol.ReservedToolPrefix"/> namespace closes the
    /// other half of that gap, by stopping a contributed tool from *looking* built-in.
    /// </para>
    /// <para>
    /// The registry is read live on every call rather than cached, which is what lets a tool
    /// registered after this server started be callable without a restart. The cost is one
    /// dictionary lookup per miss, which is not worth optimising away.
    /// </para>
    /// </remarks>
    private bool TryResolveTool(string name, out IMcpTool tool)
    {
        if (_tools.TryGet(name, out ToolHandler compiled))
        {
            tool = compiled;
            return true;
        }

        if (Extensibility.McpExtensionRegistry.Current.TryGet(name, out Extensibility.ProviderToolHandler contributed))
        {
            tool = contributed;
            return true;
        }

        tool = null!;
        return false;
    }
}
