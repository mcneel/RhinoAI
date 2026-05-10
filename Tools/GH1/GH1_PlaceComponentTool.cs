using System.Drawing;

using RhMcp.Resources;

using Grasshopper;
using Grasshopper.Kernel;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_PlaceComponentTool
{
    [McpServerTool(Name = "place_component")]
    [Description("Place a Grasshopper component by name onto the active GH1 canvas at the given pixel position. Returns JSON describing the new instance.")]
    public static string PlaceComponent(
        RhinoDoc _,
        [Description("Component name as it appears in the component library (e.g. 'Number Slider', 'Point', 'Addition').")] string name,
        [Description("Canvas X position in pixels.")] float x = 100,
        [Description("Canvas Y position in pixels.")] float y = 100)
    {
        if (!GH1_Utils.TryGetOrCreateDoc(out GH_Document doc))
            return "Could not get or create GH document";

        IGH_ObjectProxy? proxy = null;
        foreach (IGH_ObjectProxy p in Instances.ComponentServer.ObjectProxies)
        {
            if (string.Equals(p.Desc.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                proxy = p;
                break;
            }
        }
        if (proxy is null) return $"No component named '{name}' found";

        IGH_DocumentObject obj = proxy.CreateInstance();
        if (obj is null) return $"Failed to instantiate '{name}'";

        obj.CreateAttributes();
        obj.Attributes.Pivot = new PointF(x, y);
        doc.AddObject(obj, false);
        doc.NewSolution(false);

        return JsonSerializer.Serialize(new
        {
            id = obj.InstanceGuid,
            name = obj.Name,
            x,
            y
        });
    }
}
