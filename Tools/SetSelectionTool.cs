using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Rhino;
using Rhino.DocObjects;

namespace RhMcp.Tools;

public sealed class SetSelectionTool : IMcpTool
{
    public string Name => "set_selection";
    public string Description => "Select objects by filter (IDs, names, layer, geometry type). Clears existing selection. Times out after 5s.";
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            ids = new { type = "array", items = new { type = "string" }, description = "Object GUIDs" },
            names = new { type = "array", items = new { type = "string" }, description = "Object names" },
            layer = new { type = "string", description = "Layer full path — selects all objects on layer" },
            geometryType = new { type = "string", description = "Filter by type: point, pointset, curve, surface, brep, mesh, annotation, light, block" }
        }
    };

    public object Execute(JsonObject? args)
    {
        var ids = args?["ids"]?.AsArray().Select(n => n?.GetValue<string>()).OfType<string>().ToArray() ?? [];
        var names = args?["names"]?.AsArray().Select(n => n?.GetValue<string>()).OfType<string>().ToArray() ?? [];
        var layer = args?["layer"]?.GetValue<string>();
        var geoType = args?["geometryType"]?.GetValue<string>();

        var selected = 0;
        string? warning = null;

        var task = Task.Run(() =>
        {
            var doc = RhinoDoc.ActiveDoc;
            doc.Objects.UnselectAll();

            var guidSet = new HashSet<Guid>();
            foreach (var idStr in ids)
                if (Guid.TryParse(idStr, out var g))
                    guidSet.Add(g);

            foreach (var guid in guidSet)
            {
                var obj = doc.Objects.FindId(guid);
                if (obj != null) { obj.Select(true); selected++; }
            }

            if (names.Length > 0 || !string.IsNullOrEmpty(layer) || !string.IsNullOrEmpty(geoType))
            {
                var settings = new ObjectEnumeratorSettings
                {
                    ActiveObjects = true,
                    HiddenObjects = false,
                    LockedObjects = true,
                    DeletedObjects = false,
                    IncludeLights = true,
                    IncludeGrips = false,
                };

                if (!string.IsNullOrEmpty(geoType))
                    settings.ObjectTypeFilter = ParseObjectType(geoType);

                if (!string.IsNullOrEmpty(layer))
                {
                    var idx = doc.Layers.FindByFullPath(layer, RhinoMath.UnsetIntIndex);
                    if (idx >= 0)
                        settings.LayerIndexFilter = idx;
                    else
                        warning = $"Layer not found: {layer}";
                }

                var nameSet = names.ToHashSet(StringComparer.Ordinal);

                foreach (var obj in doc.Objects.GetObjectList(settings))
                {
                    if (nameSet.Count > 0 && !nameSet.Contains(obj.Name ?? string.Empty)) continue;
                    if (guidSet.Contains(obj.Id)) continue;
                    obj.Select(true);
                    selected++;
                }
            }

            doc.Views.Redraw();
        });

        if (!task.Wait(TimeSpan.FromSeconds(5)))
            return new { content = new[] { new { type = "text", text = "Timeout: selection exceeded 5 seconds." } } };

        if (task.IsFaulted)
            return new { content = new[] { new { type = "text", text = $"Error: {task.Exception?.GetBaseException().Message}" } } };

        var msg = warning is null
            ? $"Selected {selected} object(s)."
            : $"Selected {selected} object(s). Warning: {warning}";
        return new { content = new[] { new { type = "text", text = msg } } };
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
