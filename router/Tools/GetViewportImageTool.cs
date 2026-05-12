using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools;

// NOTE: the plugin returns multimodal content (text metadata + JPEG image) for this tool.
// The router currently proxies it as a JSON string — the embedded image base64 ends up
// nested inside a text content rather than as a proper MCP image content. Functional but
// suboptimal. Revisit by parsing the upstream result and returning IEnumerable<AIContent>.
[McpServerToolType]
public class GetViewportImageTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "get_viewport_image")]
    [Description("Capture the active Rhino viewport as JPG. Returns the image plus a JSON metadata block describing the resulting camera, display mode, framed scene bounds, and on-screen object count — use the metadata to diagnose empty/off-screen captures without re-shooting.")]
    public Task<string> GetViewportImageAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Image width pixels (default 480) (max 1280) increase sparingly")] int width = 480,
        [Description("Image height pixels (default 270) (max 720) increase sparingly")] int height = 270,
        [Description("Standard view: top, bottom, left, right, front, back, perspective")] string? view = null,
        [Description("Display mode by English name: Wireframe, Shaded, Rendered, Ghosted, X-Ray, Technical, Artistic, Pen, Monochrome, Arctic, Raytraced")] string? displayMode = null,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "get_viewport_image",
            new { width, height, view, displayMode }, ct);
    }
}
