using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class SetLayerMaterialTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "set_layer_material")]
    [Description("Set the render material on a layer. Accepts diffuse color, transparency, and gloss. Optionally also sets the layer display color.")]
    public Task<string> SetLayerMaterialAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Layer full path")] string layer,
        [Description("Diffuse color hex like '#FF0000' or known color name")] string? color = null,
        [Description("Transparency 0.0 (opaque) to 1.0 (fully transparent)")] double? transparency = null,
        [Description("Glossiness 0.0 (matte) to 1.0 (mirror)")] double? gloss = null,
        [Description("Also apply color as the layer display (wireframe) color")] bool applyToLayerColor = true,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "set_layer_material",
            new { layer, color, transparency, gloss, applyToLayerColor }, ct);
    }
}
