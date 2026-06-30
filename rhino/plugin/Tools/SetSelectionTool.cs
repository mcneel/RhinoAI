using Rhino.DocObjects;

namespace RhMcp.Tools;

[McpServerToolType]
public static class SetSelectionTool
{
    [McpServerTool("set_selection", "Set Selection", false, false)]
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
        var warnings = new List<string>();

        doc.Objects.UnselectAll();

        var guidSet = new HashSet<Guid>();
        var malformedIds = new List<string>();
        foreach (var idStr in ids)
        {
            if (Guid.TryParse(idStr, out var g))
                guidSet.Add(g);
            else
                malformedIds.Add(idStr);
        }

        var unmatchedGuids = 0;
        foreach (var guid in guidSet)
        {
            var obj = doc.Objects.FindId(guid);
            if (obj != null) { obj.Select(true); selected++; }
            else unmatchedGuids++;
        }

        if (malformedIds.Count > 0)
            warnings.Add($"Malformed GUID(s) skipped: {string.Join(", ", malformedIds)}");
        if (unmatchedGuids > 0)
            warnings.Add($"{unmatchedGuids} GUID(s) did not match any object");

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

            bool typeResolved = true;
            if (!string.IsNullOrEmpty(geometryType))
            {
                if (TryParseObjectType(geometryType, out ObjectType objectType))
                {
                    settings.ObjectTypeFilter = objectType;
                }
                else
                {
                    warnings.Add($"Unknown geometry type: {geometryType}");
                    typeResolved = false;
                }
            }

            bool layerResolved = true;
            if (!string.IsNullOrEmpty(layer))
            {
                var idx = doc.Layers.FindByFullPath(layer, RhinoMath.UnsetIntIndex);
                if (idx >= 0)
                {
                    settings.LayerIndexFilter = idx;
                }
                else
                {
                    warnings.Add($"Layer not found: {layer}");
                    layerResolved = false;
                }
            }

            var nameSet = names.ToHashSet(StringComparer.Ordinal);

            // If a layer or geometry-type filter was specified but failed to
            // resolve, fall through with zero matches rather than selecting
            // every object in the document.
            if (layerResolved && typeResolved)
            {
                foreach (var obj in doc.Objects.GetObjectList(settings))
                {
                    if (nameSet.Count > 0 && !nameSet.Contains(obj.Name ?? string.Empty)) continue;
                    if (guidSet.Contains(obj.Id)) continue;
                    obj.Select(true);
                    selected++;
                }
            }
        }

        doc.Views.Redraw();

        return warnings.Count == 0
            ? $"Selected {selected} object(s)."
            : $"Selected {selected} object(s). Warning: {string.Join("; ", warnings)}";
    }

    private static bool TryParseObjectType(string s, out ObjectType objectType)
    {
        switch (s.ToLowerInvariant())
        {
            case "point": objectType = ObjectType.Point; return true;
            case "pointset": objectType = ObjectType.PointSet; return true;
            case "curve": objectType = ObjectType.Curve; return true;
            case "surface": objectType = ObjectType.Surface; return true;
            case "brep": objectType = ObjectType.Brep; return true;
            case "mesh": objectType = ObjectType.Mesh; return true;
            case "annotation": objectType = ObjectType.Annotation; return true;
            case "light": objectType = ObjectType.Light; return true;
            case "block": objectType = ObjectType.InstanceReference; return true;
            default: objectType = ObjectType.None; return false;
        }
    }
}
