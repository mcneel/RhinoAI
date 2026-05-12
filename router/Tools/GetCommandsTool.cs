using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class GetCommandsTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "get_commands")]
    [Description("Discover Rhino command names available to run_command. Returns English names from all registered plugins (including those not yet loaded; invoking such a command may trigger plugin load). Test commands are excluded. Use filter to narrow the list before calling run_command.")]
    public Task<string> GetCommandsAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Substring filter (case-insensitive). Strongly recommended — unfiltered results can exceed 1000 commands.")] string? filter = null,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "get_commands", new { filter }, ct);
    }
}
