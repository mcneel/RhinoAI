using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

// Example of a proxied Rhino tool. For v1 each plugin tool is hand-mirrored here
// with a `slot` argument prepended and the body forwarded via ProxyDispatcher.
// Once all are mirrored, consider moving schemas into a shared csproj (RhMcp.Schemas).
[McpServerToolType]
public class ListObjectsTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "list_objects")]
    [Description("List all objects in the slot's active document.")]
    public Task<string> ListObjectsAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "list_objects", new { }, ct);
    }
}
