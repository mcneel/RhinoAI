using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class RunPythonTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "run_python")]
    [Description("Execute a Python 3 script. Returns JSON with stdout and error fields; error is null on success.")]
    public Task<string> RunPythonAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Script")] string script,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "run_python", new { script }, ct);
    }
}
