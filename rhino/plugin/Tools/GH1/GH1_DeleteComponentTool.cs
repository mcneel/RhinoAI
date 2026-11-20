using System.Drawing;

using Rhino.AI.Resources;

using Grasshopper;
using Grasshopper.Kernel;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class GH1_DeleteComponentTool
{

    [McpServerTool("g1_delete_component", "Delete GH1 Component", false, true)]
    [Description("Delete a Grasshopper component on the active GH1 canvas.")]
    public static IToolResult Delete(
        RhinoDoc rhDoc,
        [Description("Component instance Guid ")] string selector,
        [Description("If true, trigger a new solution after removing.")] bool solve = false)
    {
        if (!GH1_Utils.TryGetOrCreateDoc(rhDoc, out GH_Document doc))
            return GH1_Failures.NoDocument;

        if (!Guid.TryParse(selector, out Guid id))
            return Failure(ToolError.BadArgument, $"Could not parse guid {selector}");

        IGH_DocumentObject obj = doc.FindObject(id, true);
        if (obj is null)
            return Failure(ToolError.GH_Object_NotFound, $"No component with id '{id}' found");

        return DeleteComponent(doc, obj, solve);
    }

    private static IToolResult DeleteComponent(GH_Document doc, IGH_DocumentObject obj, bool solve)
    {
        bool removed = doc.RemoveObject(obj, false);
        if (solve)
            doc.NewSolution(false);

        return removed
            ? Success(ContentBlock.CreateText("Deleted"))
            : Failure(ToolError.Failed, "Failed to delete component", "Unknown");
    }
}
