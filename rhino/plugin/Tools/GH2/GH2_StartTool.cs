using RhinoAI.Resources;

namespace RhinoAI.Tools;

[McpServerToolType]
public static class GH2_StartTool
{

    private static Guid GH2_PlugInId { get; } = new("8307876d-a461-4daa-bb77-eb3715925513");

    [McpServerTool("g2_start", "Start Grasshopper 2", false, false)]
    [Description("Starts GH2")]
    public static string Launch(RhinoDoc doc)
    {
        if (RhinoApp.Version.Major < 9)
            return "GH2 is not installed";
        try
        {
            string commandName = Rhino.Commands.Command.IsCommand("_G2") ? "_G2" : "_GH2";
            RhinoApp.RunScript(doc.RuntimeSerialNumber, commandName, true);
            return Verify(doc);
        }
        catch (Exception ex)
        {
            return $"g2_start threw: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        }
    }

    private static string Verify(RhinoDoc doc) => GH2_Utils.TryGetDoc(doc, out _) ? "Opened GH2" : "Failure opening GH2";

}
