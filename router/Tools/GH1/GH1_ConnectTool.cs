using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools.GH1;

[McpServerToolType]
public class GH1_ConnectTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "connect")]
    [Description("Wire an output parameter to an input parameter on the active GH1 canvas. 'src' and 'dst' may be a numeric index or a Name/NickName. For pure params (e.g. a slider) pass '' or '0'.")]
    public Task<string> ConnectAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Guid of the source IGH_DocumentObject.")] string src_id,
        [Description("Output identifier: numeric index, output Name, or output NickName. Use '' or '0' for pure params.")] string src,
        [Description("Guid of the destination IGH_DocumentObject.")] string dst_id,
        [Description("Input identifier: numeric index, input Name, or input NickName. Use '' or '0' for pure params.")] string dst,
        [Description("If true, trigger a new solution after wiring. Set false to batch multiple operations and solve once at the end.")] bool solve = true,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "connect",
            new { src_id, src, dst_id, dst, solve }, ct);
    }
}
