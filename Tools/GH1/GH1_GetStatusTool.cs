using RhMcp.Resources;

using Grasshopper;
using Grasshopper.Kernel;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_GetStatusTool
{

    public record struct ComponentStatus(string Name, string[] Errors);

    [McpServerTool(Name = "get_status")]
    [Description("Returns all of the errors of the current GH Canvas")]
    public static string GetStatus(RhinoDoc _)
    {
        if (!GH1_Utils.TryGetDoc(out GH_Document ghDoc)) return "Could not get GHDoc";
        List<ComponentStatus> statuses = GetCanvasStatus(ghDoc);
        return JsonSerializer.Serialize(statuses);
    }

    public static List<ComponentStatus> GetCanvasStatus(GH_Document ghDoc)
    {
        List<ComponentStatus> statuses = [];

        foreach(IGH_ActiveObject obj in ghDoc.ActiveObjects())
        {
            IEnumerable<string> warningsOrWorse = obj.RuntimeMessages(GH_RuntimeMessageLevel.Warning);
            statuses.Add(new ComponentStatus(obj.Name, warningsOrWorse.ToArray()));
        }

        return statuses;
    }
}
