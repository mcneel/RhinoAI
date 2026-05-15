using RhMcp.Resources;

using Grasshopper2.Doc;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_ClearCanvasTool
{
    public record struct ClearResult(int Removed);

    [McpServerTool(Name = "clear_canvas")]
    [Description("Remove every object from the active GH2 canvas. Destructive — requires confirm=true.")]
    public static string Clear(
        RhinoDoc _,
        [Description("Must be true to actually wipe the canvas. Defaults to false as a safety guard.")] bool confirm = false,
        [Description("If true, trigger a new solution after clearing.")] bool solve = true)
    {
        if (!confirm) return "Refused: pass confirm=true to wipe the canvas.";

        if (!GH2_Utils.TryGetDoc(out Document doc))
            return "Could not get GH2 document";

        var snapshot = doc.Objects.Forwards.ToList();
        int count = snapshot.Count;

        RhinoApp.InvokeAndWait(() =>
        {
            foreach (var obj in snapshot)
                doc.Objects.Remove(obj.InstanceId);

            if (solve) doc.Solution.Start();
            GH2_Utils.Redraw();
        });

        return JsonSerializer.Serialize(new ClearResult(count));
    }
}
