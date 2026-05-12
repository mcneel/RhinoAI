using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class SpawnSlotTool(RhinoManager manager)
{
    [McpServerTool(Name = "spawn_slot")]
    [Description("Launch a new Rhino instance and return its slot ID. Pass that ID as the `slot` arg on subsequent tool calls to target this Rhino.")]
    public async Task<string> SpawnAsync(
        [Description("Rhino version: '8', '9', or 'WIP'. Omit to use the router's configured default.")]
        string? version = null,
        CancellationToken ct = default)
    {
        try
        {
            var child = await manager.SpawnAsync(version, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(child);
        }
        catch (Exception ex)
        {
            // Surface the full failure as a string so the MCP client sees it. The SDK
            // otherwise swallows exception details into a generic "An error occurred…".
            return JsonSerializer.Serialize(new
            {
                error = ex.GetType().Name,
                message = ex.Message,
                stackTrace = ex.StackTrace,
            });
        }
    }

    [McpServerTool(Name = "close_slot")]
    [Description("Close a Rhino slot gracefully. Saves nothing.")]
    public Task<bool> CloseAsync(
        [Description("Slot ID returned by spawn_slot")]
        string slot,
        CancellationToken ct = default) => manager.CloseAsync(slot, ct);

    [McpServerTool(Name = "list_slots")]
    [Description("List all currently-running Rhino slots managed by this router.")]
    public IReadOnlyCollection<ChildRhino> List() => manager.List();
}
