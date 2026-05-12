using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class SetSelectionTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "set_selection")]
    [Description("Select objects by filter (IDs, names, layer, geometry type). Clears existing selection.")]
    public Task<string> SetSelectionAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Object GUIDs")] string[]? ids = null,
        [Description("Object names")] string[]? names = null,
        [Description("Layer full path — selects all objects on layer")] string? layer = null,
        [Description("Filter by type: point, pointset, curve, surface, brep, mesh, annotation, light, block")] string? geometryType = null,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "set_selection",
            new { ids, names, layer, geometryType }, ct);
    }
}
