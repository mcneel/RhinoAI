using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rhino;

namespace RhMcp.Tools;

public sealed class GetSelectionTool : IMcpTool
{
    public string Name => "get_selection";
    public string Description => "Return all currently selected objects in Rhino.";
    public object InputSchema => new { type = "object", properties = new { } };

    public object Execute(JsonObject? args)
    {
        var doc = RhinoDoc.ActiveDoc;
        var selected = doc.Objects
            .GetSelectedObjects(includeLights: false, includeGrips: false)
            .Select(obj => new
            {
                id = obj.Id.ToString(),
                name = obj.Name ?? "",
                layer = doc.Layers[obj.Attributes.LayerIndex].FullPath,
                type = obj.Geometry?.GetType().Name ?? "Unknown"
            })
            .ToArray();

        return new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(selected) } } };
    }
}
