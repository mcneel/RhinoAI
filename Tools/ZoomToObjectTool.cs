using System;
using System.ComponentModel;

using ModelContextProtocol.Server;

using Rhino;
using Rhino.Geometry;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ZoomToObjectTool
{
    [McpServerTool(Name = "zoom_to_object")]
    [Description("Zoom the active viewport to fit one or more objects by GUID.")]
    public static string ZoomToObject(
        [Description("Object GUIDs to zoom to")] string[] ids)
    {
        var doc = RhinoDoc.ActiveDoc;
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

        vp.ZoomBoundingBox(bb);
        doc.Views.Redraw();

        return $"Zoomed to {ids.Length} object(s).";
    }
}
