using RhMcp.Resources;

using Grasshopper.Kernel;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_ClearCanvasTool
{
    public record struct ClearResult(int Removed);

    [McpServerTool(Name = "clear_canvas")]
    [Description("Remove every object from the active GH1 canvas. Destructive — requires confirm=true.")]
    public static string Clear(
        RhinoDoc _,
        [Description("Must be true to actually wipe the canvas. Defaults to false as a safety guard.")] bool confirm = false,
        [Description("If true, trigger a new solution after clearing. Set false to batch multiple operations and solve once at the end.")] bool solve = true)
    {
        if (!confirm) return "Refused: pass confirm=true to wipe the canvas.";

        if (!GH1_Utils.TryGetDoc(out GH_Document doc))
            return "Could not get GH document";

        var snapshot = doc.Objects.ToList();
        int count = snapshot.Count;

        RhinoApp.InvokeAndWait(() =>
        {
            doc.RemoveObjects(snapshot, false);
            if (solve) doc.NewSolution(true);
            GH1_Utils.Redraw();
        });

        return JsonSerializer.Serialize(new ClearResult(count));
    }
}
