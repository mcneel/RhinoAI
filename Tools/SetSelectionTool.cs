using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using ModelContextProtocol.Server;

using Rhino;
using Rhino.DocObjects;

namespace RhMcp.Tools;

[McpServerToolType]
public static class SetSelectionTool
{
    [McpServerTool(Name = "set_selection")]
    [Description("Select objects by filter (IDs, names, layer, geometry type). Clears existing selection.")]
    public static string SetSelection(
        RhinoDoc doc,
        [Description("Object GUIDs")] string[]? ids = null,
        [Description("Object names")] string[]? names = null,
        [Description("Layer full path — selects all objects on layer")] string? layer = null,
        [Description("Filter by type: point, pointset, curve, surface, brep, mesh, annotation, light, block")] string? geometryType = null)
    {
        ids ??= [];
        names ??= [];

        var selected = 0;
        string? warning = null;

        RhinoApp.InvokeAndWait(() =>
        {
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

            if (names.Length > 0 || !string.IsNullOrEmpty(layer) || !string.IsNullOrEmpty(geometryType))
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

                if (!string.IsNullOrEmpty(geometryType))
                    settings.ObjectTypeFilter = ParseObjectType(geometryType);

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

        return warning is null
            ? $"Selected {selected} object(s)."
            : $"Selected {selected} object(s). Warning: {warning}";
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
