using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class ZoomToLayerTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "zoom_to_layer")]
    [Description("Zoom the active viewport to fit all objects on a layer (full path).")]
    public Task<string> ZoomToLayerAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Layer full path")] string layer,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "zoom_to_layer", new { layer }, ct);
    }
}
