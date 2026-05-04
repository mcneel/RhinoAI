using System;
using System.Text.Json.Nodes;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhMcp.Tools;

public sealed class ZoomToLayerTool : IMcpTool
{
    public string Name => "zoom_to_layer";
    public string Description => "Zoom the active viewport to fit all objects on a layer (full path).";
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            layer = new { type = "string", description = "Layer full path" }
        },
        required = new[] { "layer" }
    };

    public object Execute(JsonObject? args)
    {
        var layerPath = args?["layer"]?.GetValue<string>()
            ?? throw new ArgumentException("Missing required arg: layer");

        var doc = RhinoDoc.ActiveDoc;
        var idx = doc.Layers.FindByFullPath(layerPath, RhinoMath.UnsetIntIndex);

        if (idx < 0)
            return new { content = new[] { new { type = "text", text = $"Layer not found: {layerPath}" } } };

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
            return new { content = new[] { new { type = "text", text = $"No geometry on layer: {layerPath}" } } };

        var vp = doc.Views.ActiveView?.ActiveViewport
            ?? throw new InvalidOperationException("No active viewport.");

        vp.ZoomBoundingBox(bb);
        doc.Views.Redraw();

        return new { content = new[] { new { type = "text", text = $"Zoomed to {count} object(s) on layer \"{layerPath}\"." } } };
    }
}
