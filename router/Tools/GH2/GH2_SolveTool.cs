using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools.GH2;

[McpServerToolType]
public class GH2_SolveTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "solve_canvas")]
    [Description("Solves the active GH2 canvas")]
    public Task<string> SolveCanvasAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "solve_canvas", new { }, ct);
    }
}
