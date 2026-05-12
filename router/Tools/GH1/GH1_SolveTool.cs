using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools.GH1;

[McpServerToolType]
public class GH1_SolveTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "solve_graph")]
    [Description("Solves the active GH canvas. zoom_views controls whether Rhino viewports zoom to the new preview: true=always, false=never, null=auto (zoom only when nothing was previewed before the solve).")]
    public Task<string> SolveAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Auto-zoom every Rhino viewport to the GH preview after solving. true=always, false=never, null=zoom only when nothing was visible pre-solve.")] bool? zoom_views = null,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "solve_graph", new { zoom_views }, ct);
    }
}
