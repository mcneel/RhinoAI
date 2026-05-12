using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

// Each proxied tool mirrors the plugin's tool signature, prepends a `slot` arg,
// and forwards through ProxyDispatcher. Descriptions intentionally match the
// plugin's so Claude sees identical schemas regardless of which side answers.
[McpServerToolType]
public class ListObjectsTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "list_objects")]
    [Description("List objects in the active document. Filter by name, layer, or geometry type. Pure query — does not change selection or viewport.")]
    public Task<string> ListObjectsAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Object names to match")] string[]? names = null,
        [Description("Layer full path")] string? layer = null,
        [Description("Filter by type: point, pointset, curve, surface, brep, mesh, annotation, light, block")] string? geometryType = null,
        [Description("Include hidden objects (default false)")] bool includeHidden = false,
        [Description("Include locked objects (default true)")] bool includeLocked = true,
        [Description("Maximum number of objects to return (default 1000)")] int limit = 1000,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "list_objects",
            new { names, layer, geometryType, includeHidden, includeLocked, limit }, ct);
    }
}
