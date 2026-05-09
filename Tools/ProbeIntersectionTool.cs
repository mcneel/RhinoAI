using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;

using ModelContextProtocol.Server;

using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ProbeIntersectionTool
{
    [McpServerTool(Name = "probe_intersection")]
    [Description("Compute intersection points between a line segment and a Brep. Returns hit points and overlap-curve count.")]
    public static string ProbeIntersection(
        RhinoDoc doc,
        [Description("Line start {x,y,z}")] Vector3d start,
        [Description("Line end {x,y,z}")] Vector3d end,
        [Description("Target Brep GUID")] string brepId,
        [Description("Optional intersection tolerance (defaults to document absolute tolerance)")] double? tolerance = null)
    {
        if (!Guid.TryParse(brepId, out var guid))
            return JsonSerializer.Serialize(new { error = "Invalid GUID." });

        var obj = doc.Objects.FindId(guid);
        if (obj is null)
            return JsonSerializer.Serialize(new { error = "Object not found." });

        if (obj.Geometry is not Brep brep)
            return JsonSerializer.Serialize(new { error = $"Object is not a Brep (got {obj.Geometry?.GetType().Name})." });

        var startPt = (Point3d)start;
        var endPt = (Point3d)end;
        if (startPt.DistanceTo(endPt) <= 0)
            return JsonSerializer.Serialize(new { error = "Line has zero length." });

        var curve = new Line(startPt, endPt).ToNurbsCurve();
        var tol = tolerance ?? doc.ModelAbsoluteTolerance;

        if (!Intersection.CurveBrep(curve, brep, tol, out Curve[] overlap, out Point3d[] points))
            return JsonSerializer.Serialize(new { error = "Intersection routine failed." });

        var hits = points.Select(p => new
        {
            x = p.X,
            y = p.Y,
            z = p.Z,
            t = curve.ClosestPoint(p, out double param) ? param : (double?)null,
        }).ToArray();

        return JsonSerializer.Serialize(new
        {
            hits,
            hitCount = hits.Length,
            overlapCurves = overlap.Length,
            tolerance = tol,
        });
    }
}
