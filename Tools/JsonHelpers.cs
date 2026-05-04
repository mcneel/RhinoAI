using System.Text.Json.Nodes;
using Rhino.Geometry;

namespace RhMcp.Tools;

internal static class JsonHelpers
{
    public static Point3d? ParsePoint(JsonNode? node)
    {
        if (node is null) return null;
        return new Point3d(
            node["x"]?.GetValue<double>() ?? 0,
            node["y"]?.GetValue<double>() ?? 0,
            node["z"]?.GetValue<double>() ?? 0);
    }

    public static Point2d? ParsePoint2d(JsonNode? node)
    {
        if (node is null) return null;
        return new Point2d(
            node["x"]?.GetValue<double>() ?? 0,
            node["y"]?.GetValue<double>() ?? 0);
    }
}
