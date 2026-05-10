using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;

using ModelContextProtocol.Server;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhMcp.Resources;

[McpServerResourceType]
public static class SceneContextResource
{
    [McpServerResource(Name = "scene_context", UriTemplate = "rhino://scene-context",
        Title = "Scene Context", MimeType = "application/json")]
    [Description("Current Rhino document state: units, tolerances, layer tree, object summary, and bounding box. Read this before creating or modifying geometry.")]
    public static string GetSceneContext(RhinoDoc doc)
    {
        object? result = null;

        RhinoApp.InvokeAndWait(() =>
        {
            var settings = new ObjectEnumeratorSettings
            {
                ActiveObjects = true,
                HiddenObjects = false,
                DeletedObjects = false,
            };

            var objects = doc.Objects.GetObjectList(settings).ToArray();

            // Single pass: compute bbox, type counts, and per-layer counts
            var bbox = BoundingBox.Empty;
            var typeCounts = new Dictionary<string, int>();
            var layerCounts = new Dictionary<int, int>();

            foreach (var obj in objects)
            {
                var bb = obj.Geometry.GetBoundingBox(true);
                if (bb.IsValid) bbox.Union(bb);

                var typeName = obj.ObjectType.ToString();
                typeCounts[typeName] = typeCounts.TryGetValue(typeName, out int tc) ? tc + 1 : 1;

                var li = obj.Attributes.LayerIndex;
                layerCounts[li] = layerCounts.TryGetValue(li, out int lc) ? lc + 1 : 1;
            }

            var layers = doc.Layers
                .Where(l => !l.IsDeleted)
                .Select(l => new
                {
                    name = l.FullPath,
                    visible = l.IsVisible,
                    locked = l.IsLocked,
                    color = $"#{l.Color.R:X2}{l.Color.G:X2}{l.Color.B:X2}",
                    objectCount = layerCounts.TryGetValue(l.Index, out int c) ? c : 0,
                })
                .ToArray();

            result = new
            {
                document = new
                {
                    name = doc.Name ?? "Untitled",
                    path = doc.Path ?? "",
                    units = doc.ModelUnitSystem.ToString(),
                    absoluteTolerance = doc.ModelAbsoluteTolerance,
                    angleTolerance = doc.ModelAngleToleranceDegrees,
                },
                scene = new
                {
                    objectCount = objects.Length,
                    typeCounts,
                    boundingBox = bbox.IsValid ? new
                    {
                        min = new[] { bbox.Min.X, bbox.Min.Y, bbox.Min.Z },
                        max = new[] { bbox.Max.X, bbox.Max.Y, bbox.Max.Z },
                    } : null,
                },
                layers,
            };
        });

        return JsonSerializer.Serialize(result);
    }
}
