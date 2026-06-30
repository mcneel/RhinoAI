using Rhino.Geometry;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ZoomToObjectTool
{
    [McpServerTool("zoom_to_object", "Zoom To Object", false, false)]
    [Description("Zoom the active viewport to fit one or more objects by GUID.")]
    public static string ZoomToObject(
        RhinoDoc doc,
        [Description("Object GUIDs to zoom to")] string[] ids)
    {
        if (doc.IsHeadless)
        {
            return "Cannot zoom in headless doc";
        }
        
        BoundingBox bb = BoundingBox.Empty;

        foreach (string idStr in ids)
        {
            if (!Guid.TryParse(idStr, out Guid guid)) continue;
            Rhino.DocObjects.RhinoObject obj = doc.Objects.FindId(guid);
            if (obj?.Geometry == null) continue;
            bb.Union(obj.Geometry.GetBoundingBox(true));
        }

        if (!bb.IsValid)
            return "No valid objects found.";

        var vp = doc.Views.ActiveView?.ActiveViewport
            ?? throw new InvalidOperationException("No active viewport.");

        vp.ZoomBoundingBox(bb);
        doc.Views.Redraw();

        return $"Zoomed to {ids.Length} object(s).";
    }
}
