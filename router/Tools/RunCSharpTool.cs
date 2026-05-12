using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class RunCSharpTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "run_csharp")]
    [Description("Execute a C# script. Returns JSON with stdout and error fields; error is null on success.")]
    public Task<string> RunCSharpAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Script")] string script,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "run_csharp", new { script }, ct);
    }
}
