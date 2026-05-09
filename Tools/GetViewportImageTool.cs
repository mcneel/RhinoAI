using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

using Microsoft.Extensions.AI;

using ModelContextProtocol.Server;

using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GetViewportImageTool
{
    public sealed record Vec3(double X, double Y, double Z);

    [McpServerTool(Name = "get_viewport_image")]
    [Description("Capture active Rhino viewport as PNG. Optionally set standard view, camera position, target point, and zoom.")]
    public static IEnumerable<AIContent> GetViewportImage(
        [Description("Image width pixels (default 640) (max 1280) increase sparingly")] int width = 640,
        [Description("Image height pixels (default 360) (max 720) increase sparingly")] int height = 360,
        [Description("Standard view: top, bottom, left, right, front, back, perspective")] string? view = null,
        [Description("Camera position {x,y,z}")] Vec3? cameraLocation = null,
        [Description("Camera look-at point {x,y,z}")] Vec3? target = null,
        [Description("Magnification factor: >1 zoom in, 0<x<1 zoom out")] double? zoom = null)
    {
        width = Math.Min(width, 1280);
        height = Math.Min(height, 720);

        var activeView = RhinoDoc.ActiveDoc?.Views.ActiveView
            ?? throw new InvalidOperationException("No active view.");

        Bitmap? bitmap = null;
        RhinoApp.InvokeAndWait(() =>
        {
            var vp = activeView.ActiveViewport;

            if (!string.IsNullOrEmpty(view))
            {
                var proj = view.ToLowerInvariant() switch
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

            if (cameraLocation is not null)
                vp.SetCameraLocation(new Point3d(cameraLocation.X, cameraLocation.Y, cameraLocation.Z), false);

            if (target is not null)
                vp.SetCameraTarget(new Point3d(target.X, target.Y, target.Z), false);

            if (zoom.HasValue)
                vp.Magnify(zoom.Value, true);

            activeView.Redraw();

            bitmap = activeView.CaptureToBitmap(new Size(width, height));
        });

        if (bitmap is null) return [new DataContent("could not capture image")];

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);

        return [new DataContent(ms.ToArray(), "image/png")];
    }
}
