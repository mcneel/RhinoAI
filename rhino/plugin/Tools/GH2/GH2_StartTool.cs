using RhMcp.Resources;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_StartTool
{
    [McpServerTool(Name = "g2_start")]
    [Description("Starts GH2")]
    public static string Launch(RhinoDoc _)
    {
        if (!GH2_Utils.IsInstalled()) return "G2 is not installed";
        try
        {
            return GH2_Utils.TryGetDoc(out var __) ? "Opened G2" : "Failure opening G2";
        }
        catch (Exception ex)
        {
            return $"g2_start threw: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        }
    }

}
