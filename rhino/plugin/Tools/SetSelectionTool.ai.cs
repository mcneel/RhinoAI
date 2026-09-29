using Rhino.DocObjects;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class SetSelectionTool
{
    [McpServerTool("set_selection", "Set Selection", false, false)]
    [Description("Select objects by filter (IDs, names, layer, geometry type). Clears existing selection.")]
    public static IToolResult SetSelection(
        RhinoDoc doc,
        [Description("Object GUIDs")] string[]? ids = null,
        [Description("Object names")] string[]? names = null,
        [Description("Layer full path — selects all objects on layer")] string? layer = null,
        [Description("Filter by type: point, pointset, curve, surface, brep, mesh, annotation, light, block")] string? geometryType = null)
    {
        ids ??= [];
        names ??= [];

        int selected = 0;
        List<string> warnings = [];

        doc.Objects.UnselectAll();

        HashSet<Guid> guidSet = [];
        List<string> malformedIds = [];
        foreach (string idStr in ids)
        {
            if (Guid.TryParse(idStr, out Guid g))
                guidSet.Add(g);
            else
                malformedIds.Add(idStr);
        }

        int unmatchedGuids = 0;
        foreach (Guid guid in guidSet)
        {
            RhinoObject obj = doc.Objects.FindId(guid);
            if (obj is not null)
            {
                obj.Select(true);
                selected++;
            }
            else
            {
                unmatchedGuids++;
            }
        }

        if (malformedIds.Count > 0)
            warnings.Add($"Malformed GUID(s) skipped: {string.Join(", ", malformedIds)}");
        if (unmatchedGuids > 0)
            warnings.Add($"{unmatchedGuids} GUID(s) did not match any object");

        if (names.Length > 0 || !string.IsNullOrEmpty(layer) || !string.IsNullOrEmpty(geometryType))
        {
            ObjectEnumeratorSettings settings = new()
            {
                ActiveObjects = true,
                HiddenObjects = false,
                LockedObjects = true,
                DeletedObjects = false,
                IncludeLights = true,
                IncludeGrips = false,
            };

            bool typeResolved = true;
            if (!String.IsNullOrEmpty(geometryType))
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
                int idx = doc.Layers.FindByFullPath(layer, RhinoMath.UnsetIntIndex);
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

            HashSet<string> nameSet = names.ToHashSet(StringComparer.Ordinal);

            // If a layer or geometry-type filter was specified but failed to
            // resolve, fall through with zero matches rather than selecting
            // every object in the document.
            if (layerResolved && typeResolved)
            {
                foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
                {
                    if (nameSet.Count > 0 && !nameSet.Contains(obj.Name ?? string.Empty)) continue;
                    if (guidSet.Contains(obj.Id)) continue;
                    obj.Select(true);
                    selected++;
                }
            }
        }

        doc.Views.Redraw();

        return Success(
            ContentBlock.CreateText($"Selected {selected} object(s)."),
            warnings.Count == 0 ? null : string.Join("; ", warnings));
    }

    private static bool TryParseObjectType(string s, out ObjectType objectType)
    {
        objectType = s.ToLowerInvariant() switch
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

            _ => ObjectType.None,
        };

        return objectType is not ObjectType.None;
    }
}
