using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace RhMcp.Router.Tools.GH1;

[McpServerToolType]
public class GH1_ConnectManyTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "connect_many")]
    [Description("Wire multiple output→input connections in one call. Same selector semantics as 'connect' (numeric index or Name/NickName; '' or '0' for pure params). A failed wire does not stop later ones; per-wire results are returned. solve runs once at the end.")]
    public Task<string> ConnectManyAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Array of {SrcId, Src, DstId, Dst} wire descriptors.")] JsonElement wires,
        [Description("If true, trigger a new solution after wiring. Set false to batch further.")] bool solve = true,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "connect_many", new { wires, solve }, ct);
    }
}
