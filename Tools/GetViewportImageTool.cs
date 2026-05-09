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

    [McpServerTool(Name = "get_viewport_image")]
    [Description("Capture the active Rhino viewport as PNG. By default does not mutate viewport state — any view, camera, or display-mode change is reverted after capture. Set restoreState=false to make changes persistent.")]
    public static IEnumerable<AIContent> GetViewportImage(
        [Description("Image width pixels (default 480) (max 1280) increase sparingly")] int width = 480,
        [Description("Image height pixels (default 270) (max 720) increase sparingly")] int height = 270,
        [Description("Standard view: top, bottom, left, right, front, back, perspective")] string? view = null,
        [Description("Display mode by English name: Wireframe, Shaded, Rendered, Ghosted, X-Ray, Technical, Artistic, Pen, Monochrome, Arctic, Raytraced")] string? displayMode = null,
        [Description("Camera position {x,y,z}")] Vector3d? cameraLocation = null,
        [Description("Camera look-at point {x,y,z}")] Vector3d? target = null,
        [Description("Frame this bounding box (min corner). Pair with boxMax. Replaces zoom — agent supplies what to frame, tool computes how far back to stand.")] Vector3d? boxMin = null,
        [Description("Frame this bounding box (max corner). Pair with boxMin.")] Vector3d? boxMax = null,
        [Description("Magnification factor: >1 zoom in, 0<x<1 zoom out. Applied after boxMin/boxMax if both supplied.")] double? zoom = null,
        [Description("Revert view, camera, and display-mode changes after capture (default true). Set false to permanently change the user's viewport.")] bool restoreState = true)
    {
        width = Math.Min(width, 1280);
        height = Math.Min(height, 720);

        var activeView = RhinoDoc.ActiveDoc?.Views.ActiveView
            ?? throw new InvalidOperationException("No active view.");

        Bitmap? bitmap = null;
        string? error = null;

        RhinoApp.InvokeAndWait(() =>
        {
            var vp = activeView.ActiveViewport;

            // Snapshot for restoration
            var savedTarget = vp.CameraTarget;
            var savedLocation = vp.CameraLocation;
            var savedUp = vp.CameraUp;
            var savedLens = vp.Camera35mmLensLength;
            var savedIsParallel = vp.IsParallelProjection;
            var savedDisplayMode = vp.DisplayMode;

            try
            {
                if (!string.IsNullOrEmpty(view))
                {
                    var proj = ParseProjection(view);
                    if (proj == DefinedViewportProjection.None)
                    {
                        error = $"Unknown view: {view}";
                        return;
                    }
                    vp.SetProjection(proj, null, true);
                }

                if (!string.IsNullOrEmpty(displayMode))
                {
                    var mode = FindDisplayMode(displayMode);
                    if (mode is null)
                    {
                        error = $"Unknown display mode: {displayMode}";
                        return;
                    }
                    vp.DisplayMode = mode;
                }

                if (cameraLocation is not null)
                    vp.SetCameraLocation((Point3d)cameraLocation, false);

                if (target is not null)
                    vp.SetCameraTarget((Point3d)target, false);

                if (boxMin is not null && boxMax is not null)
                {
                    var bb = new BoundingBox((Point3d)boxMin, (Point3d)boxMax);
                    if (bb.IsValid)
                        vp.ZoomBoundingBox(bb);
                    else
                    {
                        error = "boxMin/boxMax do not form a valid bounding box.";
                        return;
                    }
                }

                if (zoom.HasValue)
                    vp.Magnify(zoom.Value, true);

                activeView.Redraw();

                bitmap = activeView.CaptureToBitmap(new Size(width, height));
            }
            finally
            {
                if (restoreState)
                {
                    if (savedIsParallel)
                        vp.ChangeToParallelProjection(true);
                    else
                        vp.ChangeToPerspectiveProjection(true, savedLens > 0 ? savedLens : 50.0);

                    vp.SetCameraLocations(savedTarget, savedLocation);
                    vp.CameraUp = savedUp;
                    if (savedDisplayMode is not null) vp.DisplayMode = savedDisplayMode;
                    activeView.Redraw();
                }
            }
        });

        if (error is not null) return [new DataContent(error)];
        if (bitmap is null) return [new DataContent("could not capture image")];

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);

        return [new DataContent(ms.ToArray(), "image/png")];
    }

    private static DefinedViewportProjection ParseProjection(string s) => s.ToLowerInvariant() switch
    {
        "top" => DefinedViewportProjection.Top,
        "bottom" => DefinedViewportProjection.Bottom,
        "left" => DefinedViewportProjection.Left,
        "right" => DefinedViewportProjection.Right,
        "front" => DefinedViewportProjection.Front,
        "back" => DefinedViewportProjection.Back,
        "perspective" => DefinedViewportProjection.Perspective,
        _ => DefinedViewportProjection.None,
    };

    private static DisplayModeDescription? FindDisplayMode(string name)
    {
        foreach (var mode in DisplayModeDescription.GetDisplayModes())
        {
            if (string.Equals(mode.EnglishName, name, StringComparison.OrdinalIgnoreCase))
                return mode;
        }
        return null;
    }
}
