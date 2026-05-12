using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools.GH2;

[McpServerToolType]
public class GH2_StartTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "start_gh2")]
    [Description("Starts GH2")]
    public Task<string> LaunchAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "start_gh2", new { }, ct);
    }
}
