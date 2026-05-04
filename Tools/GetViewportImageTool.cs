using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json.Nodes;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace RhMcp.Tools;

public sealed class GetViewportImageTool : IMcpTool
{
    public string Name => "get_viewport_image";
    public string Description => "Capture active Rhino viewport as PNG. Optionally set standard view, camera position, target point, and zoom.";
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            width = new { type = "integer", description = "Image width pixels (default 640) (max 1280) increase sparingly" },
            height = new { type = "integer", description = "Image height pixels (default 360) (max 720) increase sparingly" },
            view = new { type = "string", description = "Standard view: top, bottom, left, right, front, back, perspective" },
            cameraLocation = new
            {
                type = "object",
                description = "Camera position {x,y,z}",
                properties = new { x = new { type = "number" }, y = new { type = "number" }, z = new { type = "number" } }
            },
            target = new
            {
                type = "object",
                description = "Camera look-at point {x,y,z}",
                properties = new { x = new { type = "number" }, y = new { type = "number" }, z = new { type = "number" } }
            },
            zoom = new { type = "number", description = "Magnification factor: >1 zoom in, 0<x<1 zoom out" }
        }
    };

    public object Execute(JsonObject? args)
    {
        var width = Math.Max(args?["width"]?.GetValue<int>() ?? 640, 1280);
        var height = Math.Max(args?["height"]?.GetValue<int>() ?? 360, 720);
        var viewName = args?["view"]?.GetValue<string>();
        var camLoc = ParsePoint(args?["cameraLocation"]);
        var target = ParsePoint(args?["target"]);
        var zoom = args?["zoom"]?.GetValue<double>();

        var view = RhinoDoc.ActiveDoc?.Views.ActiveView
            ?? throw new InvalidOperationException("No active view.");

        var vp = view.ActiveViewport;

        if (!string.IsNullOrEmpty(viewName))
        {
            var proj = viewName.ToLowerInvariant() switch
            {
                "top" => DefinedViewportProjection.Top,
                "bottom" => DefinedViewportProjection.Bottom,
                "left" => DefinedViewportProjection.Left,
                "right" => DefinedViewportProjection.Right,
                "front" => DefinedViewportProjection.Front,
                "back" => DefinedViewportProjection.Back,
                "perspective" => DefinedViewportProjection.Perspective,
                _ => DefinedViewportProjection.None
            };
            if (proj != DefinedViewportProjection.None)
                vp.SetProjection(proj, null, true);
        }

        if (camLoc.HasValue)
            vp.SetCameraLocation(camLoc.Value, false);

        if (target.HasValue)
            vp.SetCameraTarget(target.Value, false);

        if (zoom.HasValue)
            vp.Magnify(zoom.Value, true);

        view.Redraw();

        using var bmp = view.CaptureToBitmap(new Size(width, height));
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        var base64 = Convert.ToBase64String(ms.ToArray());

        return new { content = new[] { new { type = "image", data = base64, mimeType = "image/png" } } };
    }

    private static Point3d? ParsePoint(JsonNode? node)
    {
        if (node is null) return null;
        return new Point3d(
            node["x"]?.GetValue<double>() ?? 0,
            node["y"]?.GetValue<double>() ?? 0,
            node["z"]?.GetValue<double>() ?? 0);
    }
}
