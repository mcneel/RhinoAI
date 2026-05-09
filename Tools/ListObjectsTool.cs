using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;

using ModelContextProtocol.Server;

using Rhino;
using Rhino.DocObjects;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ListObjectsTool
{
    [McpServerTool(Name = "list_objects")]
    [Description("List objects in the active document. Filter by name, layer, or geometry type. Pure query — does not change selection or viewport.")]
    public static string ListObjects(
        RhinoDoc doc,
        [Description("Object names to match")] string[]? names = null,
        [Description("Layer full path")] string? layer = null,
        [Description("Filter by type: point, pointset, curve, surface, brep, mesh, annotation, light, block")] string? geometryType = null,
        [Description("Include hidden objects (default false)")] bool includeHidden = false,
        [Description("Include locked objects (default true)")] bool includeLocked = true,
        [Description("Maximum number of objects to return (default 1000)")] int limit = 1000)
    {
        var settings = new ObjectEnumeratorSettings
        {
            ActiveObjects = true,
            HiddenObjects = includeHidden,
            LockedObjects = includeLocked,
            DeletedObjects = false,
            IncludeLights = true,
            IncludeGrips = false,
        };

        if (!string.IsNullOrEmpty(geometryType))
            settings.ObjectTypeFilter = ParseObjectType(geometryType);

        string? warning = null;
        if (!string.IsNullOrEmpty(layer))
        {
            var idx = doc.Layers.FindByFullPath(layer, RhinoMath.UnsetIntIndex);
            if (idx >= 0)
                settings.LayerIndexFilter = idx;
            else
                warning = $"Layer not found: {layer}";
        }

        var nameSet = (names ?? []).ToHashSet(StringComparer.Ordinal);

        var matches = doc.Objects.GetObjectList(settings)
            .Where(o => nameSet.Count == 0 || nameSet.Contains(o.Name ?? string.Empty));

        var truncated = false;
        var results = matches
            .Take(limit + 1)
            .Select(o => new
            {
                id = o.Id.ToString(),
                name = o.Name ?? string.Empty,
                layer = doc.Layers[o.Attributes.LayerIndex].FullPath,
                type = o.Geometry?.GetType().Name ?? "Unknown",
            })
            .ToArray();

        if (results.Length > limit)
        {
            truncated = true;
            results = results.Take(limit).ToArray();
        }

        return JsonSerializer.Serialize(new
        {
            count = results.Length,
            truncated,
            warning,
            objects = results,
        });
    }

    private static ObjectType ParseObjectType(string s) => s.ToLowerInvariant() switch
    {
        "point" => ObjectType.Point,
        "pointset" => ObjectType.PointSet,
        "curve" => ObjectType.Curve,
        "surface" => ObjectType.Surface,
        "brep" => ObjectType.Brep,
        "mesh" => ObjectType.Mesh,
        "annotation" => ObjectType.Annotation,
        "light" => ObjectType.Light,
        "block" => ObjectType.InstanceReference,
        _ => ObjectType.AnyObject,
    };
}
