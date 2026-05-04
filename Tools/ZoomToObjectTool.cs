using System;
using System.Linq;
using System.Text.Json.Nodes;
using Rhino;
using Rhino.Geometry;

namespace RhMcp.Tools;

public sealed class ZoomToObjectTool : IMcpTool
{
    public string Name => "zoom_to_object";
    public string Description => "Zoom the active viewport to fit one or more objects by GUID.";
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            ids = new { type = "array", items = new { type = "string" }, description = "Object GUIDs to zoom to" }
        },
        required = new[] { "ids" }
    };

    public object Execute(JsonObject? args)
    {
        var ids = args?["ids"]?.AsArray().Select(n => n?.GetValue<string>()).OfType<string>().ToArray()
            ?? throw new ArgumentException("Missing required arg: ids");

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
            return new { content = new[] { new { type = "text", text = "No valid objects found." } } };

        var vp = doc.Views.ActiveView?.ActiveViewport
            ?? throw new InvalidOperationException("No active viewport.");

        vp.ZoomBoundingBox(bb);
        doc.Views.Redraw();

        return new { content = new[] { new { type = "text", text = $"Zoomed to {ids.Length} object(s)." } } };
    }
}
