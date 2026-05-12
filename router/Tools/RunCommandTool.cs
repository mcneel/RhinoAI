using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class RunCommandTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "run_command")]
    [Description("Execute any Rhino command string and return command window output. Example: \"_Box 0,0,0 10,10,10\"")]
    public Task<string> RunCommandAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Rhino command string to execute")] string command,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "run_command", new { command }, ct);
    }
}
