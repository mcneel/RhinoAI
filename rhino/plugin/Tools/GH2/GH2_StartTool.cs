using Rhino.AI.Resources;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class GH2_StartTool
{

    private static Guid GH2_PlugInId { get; } = new("8307876d-a461-4daa-bb77-eb3715925513");

    [McpServerTool("g2_start", "Start Grasshopper 2", false, false)]
    [Description("Starts GH2")]
    public static IToolResult Launch(RhinoDoc doc)
    {
        bool result = false;
        try
        {
            string commandName = Rhino.Commands.Command.IsCommand("_G2") ? "_G2" : "_GH2";
            result = RhinoApp.RunScript(doc.RuntimeSerialNumber, commandName, true);
        }
        catch (Exception ex)
        {
            return Failure(ex, "GH2 could not be started; ask the user to start it manually");
        }

        return result
            ? Success(ContentBlock.CreateText("Opened GH2"))
            : Failure(ToolError.GH_NotAvailable, "GH2 did not open");
    }

}
