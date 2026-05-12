using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools.GH1;

[McpServerToolType]
public class GH1_PlaceComponentTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "place_component")]
    [Description("Place a Grasshopper component onto the active GH1 canvas. 'selector' may be a Guid (proxy id) or a component name. If multiple components share the name, returns an ambiguity payload listing candidates.")]
    public Task<string> PlaceAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Component Guid (proxy id) or component Name (case-insensitive).")] string selector,
        [Description("Canvas X position in pixels.")] float x = 100,
        [Description("Canvas Y position in pixels.")] float y = 100,
        [Description("If true, trigger a new solution after placing. Set false to batch multiple operations and solve once at the end.")] bool solve = true,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "place_component", new { selector, x, y, solve }, ct);
    }
}
