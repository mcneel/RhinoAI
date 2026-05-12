using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class GetSelectionTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "get_selection")]
    [Description("Return all currently selected objects in Rhino.")]
    public Task<string> GetSelectionAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "get_selection", new { }, ct);
    }
}
