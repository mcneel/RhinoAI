using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace RhMcp.Router.Tools.GH1;

[McpServerToolType]
public class GH1_ApplyGraphTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "apply_graph")]
    [Description("Place sliders + components and wire them in one call. References between objects use caller-supplied 'key' strings; the tool returns the key→Guid map. Failures in any step do not abort the rest; results report per-step status. Wire src/dst use the same selector semantics as 'connect'.")]
    public Task<string> ApplyAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Sliders to place: {Key, Min, Value, Max, Type, Name?, X, Y}. Type ∈ 'float'|'int'|'even'|'odd'.")] JsonElement sliders,
        [Description("Components to place: {Key, Selector, X, Y}. Selector is a Guid (preferred — avoids name ambiguity) or component Name.")] JsonElement components,
        [Description("Wires to create: {SrcKey, Src, DstKey, Dst}. Keys must match a slider or component key above.")] JsonElement wires,
        [Description("If true, trigger a new solution at the end.")] bool solve = true,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "apply_graph",
            new { sliders, components, wires, solve }, ct);
    }
}
