using Rhino.Display;
using Rhino.Geometry;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class SetCameraTool
{
    internal const double LensMinimum = 1;

    internal const double LensMaximum = 10_000;

    [McpServerTool("set_camera", "Set Camera", false, false)]
    [Description("Set the active viewport camera. Any subset of position, target, up vector, lens length, projection, or framing bounding-box may be supplied.")]
    public static IToolResult SetCamera(
        RhinoDoc doc,
        [Description("Camera position {x,y,z}")] Vector3d? location = null,
        [Description("Camera look-at point {x,y,z}")] Vector3d? target = null,
        [Description("Camera up vector {x,y,z}")] Vector3d? up = null,
        [Description("35mm-equivalent lens length (perspective only)")] double? lensLength = null,
        [Description("Projection: 'parallel' or 'perspective'")] string? projection = null,
        [Description("Frame this bounding box (min corner). Pair with boxMax. Applied last so it dominates location/target if both supplied.")] Vector3d? boxMin = null,
        [Description("Frame this bounding box (max corner). Pair with boxMin.")] Vector3d? boxMax = null)
    {
        RhinoView? view = doc.Views.ActiveView;
        if (view is null)
            return Failure(ToolError.RH_View_NotFound, guidance: "Ask the user to open a viewport");

        RhinoViewport vp = view.ActiveViewport;
        Coercions coerced = new();

        if (!String.IsNullOrEmpty(projection))
        {
            if (projection.Equals("parallel", StringComparison.OrdinalIgnoreCase))
                vp.ChangeToParallelProjection(true);
            else if (projection.Equals("perspective", StringComparison.OrdinalIgnoreCase))
                vp.ChangeToPerspectiveProjection(true, vp.Camera35mmLensLength > 0 ? vp.Camera35mmLensLength : 50.0);
            else
                return Failure(ToolError.BadArgument, $"Unknown projection: {projection}", "Use 'parallel' or 'perspective'");
        }

        if (location is not null)
            vp.SetCameraLocation((Point3d)location, false);

        if (target is not null)
            vp.SetCameraTarget((Point3d)target, false);

        if (up is not null)
            vp.CameraUp = (Vector3d)up;
        

        if (lensLength is > 0)
            vp.Camera35mmLensLength = coerced.Clamp(nameof(lensLength), lensLength.Value, LensMinimum, LensMaximum);
        else if (lensLength.HasValue)
            coerced.Note($"{nameof(lensLength)} {lensLength.Value} is not positive, so it was left unchanged");

        if (boxMin is null != boxMax is null)
            return Failure(ToolError.BadArgument, "boxMin and boxMax must be supplied together", "Pass both corners, or neither");

        if (boxMin is not null && boxMax is not null)
        {
            BoundingBox bb = new((Point3d)boxMin, (Point3d)boxMax);
            if (bb.IsValid)
                vp.ZoomBoundingBox(bb);
            else
                return Failure(ToolError.BadArgument, "boxMin/boxMax do not form a valid bounding box.");
        }

        view.Redraw();

        return Success(ContentBlock.CreateText("Camera updated."), coerced.Guidance);
    }
}
