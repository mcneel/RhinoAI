using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools.GH1;

[McpServerToolType]
public class GH1_PlaceSliderTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "place_slider")]
    [Description("Place a Number Slider on the active GH1 canvas with the given range and current value. type: 'float' | 'int' | 'even' | 'odd'.")]
    public Task<string> PlaceAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Minimum slider value.")] double min,
        [Description("Initial slider value.")] double value,
        [Description("Maximum slider value.")] double max,
        [Description("Canvas X position in pixels.")] float x = 100,
        [Description("Canvas Y position in pixels.")] float y = 100,
        [Description("Slider accuracy: 'float', 'int', 'even', or 'odd'.")] string type = "float",
        [Description("Optional NickName for the slider.")] string? name = null,
        [Description("If true, trigger a new solution after placing. Set false to batch multiple operations and solve once at the end.")] bool solve = true,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "place_slider",
            new { min, value, max, x, y, type, name, solve }, ct);
    }
}
