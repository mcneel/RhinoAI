using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GetViewportImageTool
{

    [McpServerTool(Name = "get_viewport_image")]
    [Description("Capture the active Rhino viewport as JPG. Returns the image plus a JSON metadata block describing the resulting camera, display mode, framed scene bounds, and on-screen object count — use the metadata to diagnose empty/off-screen captures without re-shooting.")]
    public static IEnumerable<ContentBlock> GetViewportImage(
        RhinoDoc doc,
        [Description("Image width pixels (default 480) (max 1280) increase sparingly")] int width = 480,
        [Description("Image height pixels (default 270) (max 720) increase sparingly")] int height = 270,
        [Description("Standard view: top, bottom, left, right, front, back, perspective")] string? view = null,
        [Description("Display mode by English name: Wireframe, Shaded, Rendered, Ghosted, X-Ray, Technical, Artistic, Pen, Monochrome, Arctic, Raytraced")] string? displayMode = null,
        [Description("Camera position {x,y,z}")] Vector3d? cameraLocation = null,
        [Description("Camera look-at point {x,y,z}")] Vector3d? target = null,
        [Description("Frame this bounding box (min corner). Pair with boxMax. Replaces zoom — agent supplies what to frame, tool computes how far back to stand.")] Vector3d? boxMin = null,
        [Description("Frame this bounding box (max corner). Pair with boxMin.")] Vector3d? boxMax = null,
        [Description("Magnification factor: >1 zoom in, 0<x<1 zoom out. Applied after boxMin/boxMax if both supplied.")] double? zoom = null)
    {
        width = Math.Min(width, 1280);
        height = Math.Min(height, 720);

        var activeView = doc.Views.ActiveView
            ?? throw new InvalidOperationException("No active view.");

        Bitmap? bitmap = null;
        string? error = null;
        CaptureMetadata? meta = null;

        var vp = activeView.ActiveViewport;

        try
        {
            if (!string.IsNullOrEmpty(view))
            {
                var proj = ParseProjection(view);
                if (proj == DefinedViewportProjection.None)
                {
                    return [ContentBlock.CreateText(SerializeResult(meta, $"Unknown view: {view}"))];
                }
                vp.SetProjection(proj, null, true);
            }

            if (!string.IsNullOrEmpty(displayMode))
            {
                var mode = FindDisplayMode(displayMode);
                if (mode is null)
                {
                    return [ContentBlock.CreateText(SerializeResult(meta, $"Unknown display mode: {displayMode}"))];
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
                    return [ContentBlock.CreateText(SerializeResult(meta, "boxMin/boxMax do not form a valid bounding box."))];
                }
            }

            if (zoom.HasValue)
                vp.Magnify(zoom.Value, true);

            activeView.Redraw();

            meta = GatherMetadata(activeView, width, height);

            if (meta.VisibleObjectCount == 0)
            {
                return [ContentBlock.CreateText(SerializeResult(meta, "Viewport is empty — no document objects intersect the view frustum. " +
                        "Camera/target may be off the model. See metadata.scene.boundingBox for where geometry actually lives."))];
            }

            bitmap = activeView.CaptureToBitmap(new Size(width, height));
        }
        catch (Exception ex)
        {
            error = $"Capture failed: {ex.Message}";
        }

        if (error is not null)
        {
            return [ContentBlock.CreateText(SerializeResult(meta, error))];
        }
        if (bitmap is null)
        {
            return [ContentBlock.CreateText(SerializeResult(meta, "could not capture image"))];
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Jpeg);

        return
        [
            ContentBlock.CreateText(SerializeResult(meta, null)),
            ContentBlock.CreateImage(ms.ToArray(), "image/jpeg"),
        ];
    }

    private sealed class CaptureMetadata
    {
        public string ViewportName { get; set; } = "";
        public string DisplayMode { get; set; } = "";
        public string Projection { get; set; } = "";
        public double LensLength { get; set; }
        public Point3d CameraLocation { get; set; }
        public Point3d CameraTarget { get; set; }
        public Vector3d CameraUp { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public BoundingBox SceneBoundingBox { get; set; } = BoundingBox.Empty;
        public int VisibleObjectCount { get; set; }
        public int TotalObjectCount { get; set; }
    }

    private static CaptureMetadata GatherMetadata(RhinoView activeView, int width, int height)
    {
        var vp = activeView.ActiveViewport;
        var doc = activeView.Document;

        var meta = new CaptureMetadata
        {
            ViewportName = vp.Name ?? "",
            DisplayMode = vp.DisplayMode?.EnglishName ?? "",
            Projection = vp.IsPerspectiveProjection ? "perspective"
                       : vp.IsParallelProjection ? "parallel"
                       : "two-point-perspective",
            LensLength = vp.Camera35mmLensLength,
            CameraLocation = vp.CameraLocation,
            CameraTarget = vp.CameraTarget,
            CameraUp = vp.CameraUp,
            ImageWidth = width,
            ImageHeight = height,
        };

        var sceneBox = BoundingBox.Empty;
        int total = 0;
        int visible = 0;
        var pipeline = activeView.DisplayPipeline;

        var settings = new ObjectEnumeratorSettings
        {
            ActiveObjects = true,
            HiddenObjects = false,
            LockedObjects = true,
            DeletedObjects = false,
            VisibleFilter = true,
        };

        foreach (var obj in doc.Objects.GetObjectList(settings))
        {
            var bb = obj.Geometry.GetBoundingBox(true);
            if (!bb.IsValid) continue;
            total++;
            sceneBox.Union(bb);
            if (pipeline != null && pipeline.IsVisible(bb))
                visible++;
        }

        meta.SceneBoundingBox = sceneBox;
        meta.TotalObjectCount = total;
        meta.VisibleObjectCount = visible;
        return meta;
    }

    private static string SerializeResult(CaptureMetadata? meta, string? error)
    {
        var payload = new
        {
            error,
            metadata = meta is null ? null : new
            {
                viewport = new
                {
                    name = meta.ViewportName,
                    displayMode = meta.DisplayMode,
                    projection = meta.Projection,
                    width = meta.ImageWidth,
                    height = meta.ImageHeight,
                },
                camera = new
                {
                    location = XYZ(meta.CameraLocation),
                    target = XYZ(meta.CameraTarget),
                    up = XYZ((Point3d)meta.CameraUp),
                    lensLength = meta.LensLength,
                },
                scene = new
                {
                    boundingBox = meta.SceneBoundingBox.IsValid ? new
                    {
                        min = XYZ(meta.SceneBoundingBox.Min),
                        max = XYZ(meta.SceneBoundingBox.Max),
                    } : null,
                    visibleObjectCount = meta.VisibleObjectCount,
                    totalObjectCount = meta.TotalObjectCount,
                },
            },
        };
        return JsonSerializer.Serialize(payload);
    }

    private static double[] XYZ(Point3d p) => [p.X, p.Y, p.Z];

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
