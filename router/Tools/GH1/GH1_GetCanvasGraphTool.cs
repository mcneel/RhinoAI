using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools.GH1;

[McpServerToolType]
public class GH1_GetCanvasGraphTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "get_canvas_graph")]
    [Description("Return a structured snapshot of the active GH1 canvas: objects (with messages, inputs/outputs and optional volatile data summaries) and wires between them.")]
    public Task<string> GetGraphAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Include per-param volatile data summaries (branches/items/sample).")] bool include_data = true,
        [Description("How many items to include in each data sample.")] int sample_size = 3,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "get_canvas_graph", new { include_data, sample_size }, ct);
    }
}
