using RhMcp.Resources;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_SolveTool
{
    [McpServerTool(Name = "solve_graph")]
    [Description("Solves the active GH canvas. zoom_views controls whether Rhino viewports zoom to the new preview: true=always, false=never, null=auto (zoom only when nothing was previewed before the solve).")]
    public static string Solve(
        RhinoDoc rhinoDoc,
        [Description("Auto-zoom every Rhino viewport to the GH preview after solving. true=always, false=never, null=zoom only when nothing was visible pre-solve.")] bool? zoom_views = null)
    {
        if (!GH1_Utils.TryGetOrCreateDoc(out GH_Document ghDoc)) return "Could not get GHDoc";

        int activeCount = ghDoc.ActiveObjects().Count;
        if (activeCount <= 0)
        {
            return "No Active Objects";
        }

        var preBbox = GetPreviewBoundingBox(ghDoc);
        bool wasPreviewVisible = preBbox.IsValid && preBbox.Diagonal.Length > 0;

        try
        {
            RhinoApp.InvokeAndWait(() =>
            {
                ghDoc.NewSolution(true);

                bool shouldZoom = zoom_views ?? !wasPreviewVisible;
                if (shouldZoom) ZoomViewsToPreview(rhinoDoc, ghDoc);
            });
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        var statuses = GH1_Utils.GetCanvasStatus(ghDoc);
        if (statuses.Count == 0)
        {
            return "Success";
        }

        string errMsg = "Solution encountered some errors";

        return JsonSerializer.Serialize(new { errMsg, statuses });
    }

    private static BoundingBox GetPreviewBoundingBox(GH_Document doc)
    {
        var bbox = BoundingBox.Empty;
        foreach (IGH_DocumentObject obj in doc.Objects)
        {
            if (obj is IGH_PreviewObject preview && preview.IsPreviewCapable && !preview.Hidden)
            {
                try
                {
                    var b = preview.ClippingBox;
                    if (b.IsValid) bbox.Union(b);
                }
                catch { /* a few proxies throw before first solve */ }
            }
        }
        return bbox;
    }

    private static void ZoomViewsToPreview(RhinoDoc rhinoDoc, GH_Document doc)
    {
        var bbox = GetPreviewBoundingBox(doc);
        if (!bbox.IsValid || bbox.Diagonal.Length <= 0) return;

        foreach (var view in rhinoDoc.Views)
        {
            view.ActiveViewport.ZoomBoundingBox(bbox);
            view.Redraw();
        }
    }
}
