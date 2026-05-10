using Grasshopper;
using Grasshopper.Kernel;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GetComponentByNameTool
{
    [McpServerTool(Name = "get_component")]
    [Description("Return a GH component with the given name")]
    public static string GetComponent(
        RhinoDoc doc,
        [Description("Name")] string name)
    {
        GH_ComponentServer componentServer = Instances.ComponentServer;
        foreach(IGH_ObjectProxy proxy in componentServer.ObjectProxies)
        {
            if (!string.Equals(proxy.Desc.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            // TODO : Return component as useful JSON
        }
        
        return $"Component named {name} could not be found";
    }
}
