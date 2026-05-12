using RhMcp.Resources;

using Grasshopper2;
using Grasshopper2.UI;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_SolveTool
{
    [McpServerTool(Name = "solve_canvas")]
    [Description("Solves the active GH2 canvas")]
    public static string SolveCanvas(RhinoDoc _)
    {
        if (!GH2_Utils.TryGetDoc(out var ghDoc)) return "Could not get GHDoc";

        var solution = ghDoc.Solution.StartWait();

        // TODO : Return the solutoin as a nice JSON result

        return JsonSerializer.Serialize(new { /* result */ });
    }
}
