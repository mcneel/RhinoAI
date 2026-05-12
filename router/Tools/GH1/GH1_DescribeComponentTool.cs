using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools.GH1;

[McpServerToolType]
public class GH1_DescribeComponentTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "describe_component")]
    [Description("Look up a Grasshopper component by name and return its category, description, and input/output parameter list. Useful before placing or wiring components.")]
    public Task<string> DescribeAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Component name as it appears in the component library (e.g. 'Number Slider', 'Addition'). Case-insensitive.")] string name,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "describe_component", new { name }, ct);
    }
}
