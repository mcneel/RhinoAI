using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class SpawnSlotTool(RhinoManager manager)
{
    [McpServerTool(Name = "spawn_slot")]
    [Description("Launch a new Rhino instance and return its slot ID. Pass that ID as the `slot` arg on subsequent tool calls to target this Rhino.")]
    public ChildRhino Spawn(
        [Description("Rhino version: '8', '9', or 'WIP'. Omit to use the router's configured default.")]
        string? version = null)
    {
        return manager.Spawn(version);
    }

    [McpServerTool(Name = "close_slot")]
    [Description("Close a Rhino slot gracefully. Saves nothing.")]
    public bool Close(
        [Description("Slot ID returned by spawn_slot")]
        string slot) => manager.Close(slot);

    [McpServerTool(Name = "list_slots")]
    [Description("List all currently-running Rhino slots managed by this router.")]
    public IReadOnlyCollection<ChildRhino> List() => manager.List();
}
