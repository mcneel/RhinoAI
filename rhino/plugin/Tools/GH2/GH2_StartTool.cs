using RhMcp.Resources;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_StartTool
{
    
    private static Guid GH2_PlugInId { get; }= new("8307876d-a461-4daa-bb77-eb3715925513");

    [McpServerTool(Name = "g2_start")]
    [Description("Starts GH2")]
    public static string Launch(RhinoDoc _)
    {
        if (RhinoApp.Version.Major < 9) return "G2 is not installed";
        try
        {
            RhinoApp.RunScript("_G2", true);
            return Verify();
        }
        catch (Exception ex)
        {
            return $"g2_start threw: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        }
    }

    private static string Verify() => GH2_Utils.TryGetDoc(out _) ? "Opened G2" : "Failure opening G2";

}
