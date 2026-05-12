using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ZoomToLayerTool
{
    [McpServerTool(Name = "zoom_to_layer")]
    [Description("Zoom the active viewport to fit all objects on a layer (full path).")]
    public static string ZoomToLayer(
        RhinoDoc doc,
        [Description("Layer full path")] string layer)
    {
        var idx = doc.Layers.FindByFullPath(layer, RhinoMath.UnsetIntIndex);

        if (idx < 0)
            return $"Layer not found: {layer}";

        var settings = new ObjectEnumeratorSettings
        {
            ActiveObjects = true,
            HiddenObjects = true,
            LockedObjects = true,
            DeletedObjects = false,
            IncludeLights = false,
            IncludeGrips = false,
            IncludePhantoms = false,
            LayerIndexFilter = idx,
        };

        var bb = BoundingBox.Empty;
        var count = 0;

        foreach (var obj in doc.Objects.GetObjectList(settings))
        {
            if (obj.Geometry == null) continue;
            bb.Union(obj.Geometry.GetBoundingBox(true));
            count++;
        }

        if (!bb.IsValid)
            return $"No geometry on layer: {layer}";

        var vp = doc.Views.ActiveView?.ActiveViewport
            ?? throw new InvalidOperationException("No active viewport.");

        RhinoApp.InvokeAndWait(() =>
        {
            vp.ZoomBoundingBox(bb);
            doc.Views.Redraw();
        });

        return $"Zoomed to {count} object(s) on layer \"{layer}\".";
    }
}
