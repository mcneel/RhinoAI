using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class SetCameraTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "set_camera")]
    [Description("Set the active viewport camera. Any subset of position, target, up vector, lens length, projection, or framing bounding-box may be supplied.")]
    public Task<string> SetCameraAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Camera position {x,y,z}")] JsonElement? location = null,
        [Description("Camera look-at point {x,y,z}")] JsonElement? target = null,
        [Description("Camera up vector {x,y,z}")] JsonElement? up = null,
        [Description("35mm-equivalent lens length (perspective only)")] double? lensLength = null,
        [Description("Projection: 'parallel' or 'perspective'")] string? projection = null,
        [Description("Frame this bounding box (min corner). Pair with boxMax. Applied last so it dominates location/target if both supplied.")] JsonElement? boxMin = null,
        [Description("Frame this bounding box (max corner). Pair with boxMin.")] JsonElement? boxMax = null,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "set_camera",
            new { location, target, up, lensLength, projection, boxMin, boxMax }, ct);
    }
}
