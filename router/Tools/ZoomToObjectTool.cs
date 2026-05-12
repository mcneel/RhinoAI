using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class ZoomToObjectTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "zoom_to_object")]
    [Description("Zoom the active viewport to fit one or more objects by GUID.")]
    public Task<string> ZoomToObjectAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Object GUIDs to zoom to")] string[] ids,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "zoom_to_object", new { ids }, ct);
    }
}
