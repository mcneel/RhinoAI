using Rhino.Geometry;

namespace RhMcp.Tools;

[McpServerToolType]
public static class SetCameraTool
{
    [McpServerTool(Name = "set_camera")]
    [Description("Set the active viewport camera. Any subset of position, target, up vector, lens length, projection, or framing bounding-box may be supplied.")]
    public static string SetCamera(
        RhinoDoc doc,
        [Description("Camera position {x,y,z}")] Vector3d? location = null,
        [Description("Camera look-at point {x,y,z}")] Vector3d? target = null,
        [Description("Camera up vector {x,y,z}")] Vector3d? up = null,
        [Description("35mm-equivalent lens length (perspective only)")] double? lensLength = null,
        [Description("Projection: 'parallel' or 'perspective'")] string? projection = null,
        [Description("Frame this bounding box (min corner). Pair with boxMax. Applied last so it dominates location/target if both supplied.")] Vector3d? boxMin = null,
        [Description("Frame this bounding box (max corner). Pair with boxMin.")] Vector3d? boxMax = null)
    {
        var view = doc.Views.ActiveView
            ?? throw new InvalidOperationException("No active view.");

        string? error = null;

        RhinoApp.InvokeAndWait(() =>
        {
            var vp = view.ActiveViewport;

            if (!string.IsNullOrEmpty(projection))
            {
                if (projection.Equals("parallel", StringComparison.OrdinalIgnoreCase))
                    vp.ChangeToParallelProjection(true);
                else if (projection.Equals("perspective", StringComparison.OrdinalIgnoreCase))
                    vp.ChangeToPerspectiveProjection(true, vp.Camera35mmLensLength > 0 ? vp.Camera35mmLensLength : 50.0);
                else
                {
                    error = $"Unknown projection: {projection}";
                    return;
                }
            }

            if (location is not null)
                vp.SetCameraLocation((Point3d)location, false);

            if (target is not null)
                vp.SetCameraTarget((Point3d)target, false);

            if (up is not null)
                vp.CameraUp = (Vector3d)up;

            if (lensLength.HasValue)
                vp.Camera35mmLensLength = lensLength.Value;

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

            view.Redraw();
        });

        return error ?? "Camera updated.";
    }
}
