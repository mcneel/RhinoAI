using Rhino.Geometry;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ZoomToObjectTool
{
    [McpServerTool(Name = "zoom_to_object")]
    [Description("Zoom the active viewport to fit one or more objects by GUID.")]
    public static string ZoomToObject(
        RhinoDoc doc,
        [Description("Object GUIDs to zoom to")] string[] ids)
    {
        var bb = BoundingBox.Empty;

        foreach (var idStr in ids)
        {
            if (!Guid.TryParse(idStr, out var guid)) continue;
            var obj = doc.Objects.FindId(guid);
            if (obj?.Geometry == null) continue;
            bb.Union(obj.Geometry.GetBoundingBox(true));
        }

        if (!bb.IsValid)
            return "No valid objects found.";

        var vp = doc.Views.ActiveView?.ActiveViewport
            ?? throw new InvalidOperationException("No active viewport.");

        RhinoApp.InvokeAndWait(() =>
        {
            vp.ZoomBoundingBox(bb);
            doc.Views.Redraw();
        });

        return $"Zoomed to {ids.Length} object(s).";
    }
}
