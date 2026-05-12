using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class ProbeIntersectionTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "probe_intersection")]
    [Description("Compute intersection points between a line segment and a Brep. Returns hit points and overlap-curve count.")]
    public Task<string> ProbeIntersectionAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Line start {x,y,z}")] JsonElement start,
        [Description("Line end {x,y,z}")] JsonElement end,
        [Description("Target Brep GUID")] string brepId,
        [Description("Optional intersection tolerance (defaults to document absolute tolerance)")] double? tolerance = null,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "probe_intersection",
            new { start, end, brepId, tolerance }, ct);
    }
}
