using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools.GH1;

[McpServerToolType]
public class GH1_ClearCanvasTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "clear_canvas")]
    [Description("Remove every object from the active GH1 canvas. Destructive — requires confirm=true.")]
    public Task<string> ClearAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Must be true to actually wipe the canvas. Defaults to false as a safety guard.")] bool confirm = false,
        [Description("If true, trigger a new solution after clearing. Set false to batch multiple operations and solve once at the end.")] bool solve = true,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "clear_canvas", new { confirm, solve }, ct);
    }
}
