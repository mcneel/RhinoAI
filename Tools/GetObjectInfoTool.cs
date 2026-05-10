using System;
using System.ComponentModel;
using System.Text.Json;

using ModelContextProtocol.Server;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GetObjectInfoTool
{
    [McpServerTool(Name = "get_object_info")]
    [Description("Get detailed information about a single object by ID. Returns geometry type, bounding box, layer, material, and type-specific metrics (area, volume, vertex count, degree, etc.).")]
    public static string GetObjectInfo(
        RhinoDoc doc,
        [Description("Object GUID")] string id)
    {
        if (!Guid.TryParse(id, out var guid))
            return JsonSerializer.Serialize(new { error = "Invalid GUID format." });

        object? result = null;

        RhinoApp.InvokeAndWait(() =>
        {
            var obj = doc.Objects.FindId(guid);
            if (obj is null)
            {
                result = new { error = $"Object not found: {id}" };
                return;
            }

            var geo = obj.Geometry;
            var bb = geo.GetBoundingBox(true);

            var info = new
            {
                id = obj.Id.ToString(),
                name = obj.Name ?? "",
                layer = doc.Layers[obj.Attributes.LayerIndex].FullPath,
                type = obj.ObjectType.ToString(),
                geometryType = geo.GetType().Name,
                visible = obj.Visible,
                locked = obj.IsLocked,
                boundingBox = bb.IsValid ? new
                {
                    min = new[] { bb.Min.X, bb.Min.Y, bb.Min.Z },
                    max = new[] { bb.Max.X, bb.Max.Y, bb.Max.Z },
                } : null,
                metrics = GetMetrics(geo),
            };

            result = info;
        });

        return JsonSerializer.Serialize(result);
    }

    private static object? GetMetrics(GeometryBase geo)
    {
        switch (geo)
        {
            case Brep brep:
                var amp = AreaMassProperties.Compute(brep);
                var vmp = VolumeMassProperties.Compute(brep);
                return new
                {
                    faceCount = brep.Faces.Count,
                    edgeCount = brep.Edges.Count,
                    isSolid = brep.IsSolid,
                    area = amp?.Area,
                    volume = brep.IsSolid ? vmp?.Volume : null,
                };

            case Mesh mesh:
                return new
                {
                    vertexCount = mesh.Vertices.Count,
                    faceCount = mesh.Faces.Count,
                    isClosed = mesh.IsClosed,
                };

            case Curve curve:
                return new
                {
                    length = curve.GetLength(),
                    degree = curve.Degree,
                    isClosed = curve.IsClosed,
                    isPlanar = curve.IsPlanar(),
                };

            case Surface surface:
                var sAmp = AreaMassProperties.Compute(surface);
                return new
                {
                    area = sAmp?.Area,
                    degreeU = surface.Degree(0),
                    degreeV = surface.Degree(1),
                    isClosed = surface.IsClosed(0) || surface.IsClosed(1),
                };

            default:
                return null;
        }
    }
}
