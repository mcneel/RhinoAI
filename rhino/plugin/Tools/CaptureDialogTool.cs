using Microsoft.Extensions.AI;

using ModelContextProtocol;

using RhMcp.Internal;

namespace RhMcp.Tools;

[McpServerToolType]
public static class CaptureDialogTool
{
    [McpServerTool(Name = "capture_dialog")]
    [Description("Capture a PNG screenshot of the currently-foreground OS window — typically the topmost Rhino dialog. Useful when a tool call triggers a modal the agent can't see otherwise.")]
    public static IEnumerable<AIContent> CaptureDialog()
    {
        var png = DialogScreenshot.CaptureTopmost()
            ?? throw new McpException("Could not capture foreground window (no window found or unsupported platform).");

        return
        [
            new TextContent($"Captured foreground window ({png.Length} bytes PNG)."),
            new DataContent(png, "image/png"),
        ];
    }
}
