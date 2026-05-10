using RhMcp.Resources;

using Grasshopper.Kernel;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_SolveTool
{
    [McpServerTool(Name = "solve_graph")]
    [Description("Solves the active GH canvas")]
    public static string GetComponent(RhinoDoc _)
    {
        if (!GH1_Utils.TryGetDoc(out GH_Document ghDoc)) return "Could not get GHDoc";

        int activeCount = ghDoc.ActiveObjects().Count;
        if (activeCount <= 0)
        {
            return "No Active Objects";
        }

        try
        {
            ghDoc.NewSolution(true);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        var statuses = GH1_GetStatusTool.GetCanvasStatus(ghDoc);
        if (statuses.Count <= 0)
        {
            return "Success";
        }

        string errMsg = "Solution encountered some errors";

        return JsonSerializer.Serialize(new { errMsg, statuses });
    }
}
