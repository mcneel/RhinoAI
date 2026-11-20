using Rhino.AI.Resources;

using Eto.Drawing;

using Grasshopper2.Doc;
using Grasshopper2.Framework;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class GH2_DeleteComponentTool
{

    [McpServerTool("g2_delete_component", "Delete a GH2 Component", false, true)]
    [Description("Delete a GH2 component on the active canvas.")]
    public static IToolResult Delete(
        RhinoDoc rhDoc,
        [Description("Component instance guid")] string selector,
        [Description("If true, trigger a new solution after removing.")] bool solve = false)
    {
        if (!GH2_Utils.TryGetDoc(rhDoc, out Document doc))
            return GH2_Failures.NoDocument;

        if (!Guid.TryParse(selector, out Guid id))
            return Failure(ToolError.BadArgument, $"Could not parse guid {selector}");

        IDocumentObject obj = doc.Objects.Find(id);
        if (obj is null)
            return Failure(ToolError.GH_Object_NotFound, $"No component with id '{id}' found");

        return DeleteObject(doc, obj, solve);
    }

    private static IToolResult DeleteObject(Document doc, IDocumentObject obj, bool solve)
    {
        bool removed = doc.Objects.Remove(obj);
        if (solve)
            doc.Solution.Start();

        GH2_Utils.Redraw();

        return removed
            ? Success(ContentBlock.CreateText("Deleted"))
            : Failure(ToolError.Failed, "Failed to delete component", "Unknown");
    }
}
