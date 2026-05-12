using RhMcp.Resources;

using Grasshopper;
using Grasshopper.Kernel;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_DescribeComponentTool
{
    public record struct ParamInfo(string Name, string NickName, string Description, string TypeName, string Access, bool Optional);

    public record struct ComponentInfo(
        string Name,
        string NickName,
        string Description,
        string Category,
        string SubCategory,
        string Kind,
        ParamInfo[] Inputs,
        ParamInfo[] Outputs);

    [McpServerTool(Name = "describe_component")]
    [Description("Look up a Grasshopper component by name and return its category, description, and input/output parameter list. Useful before placing or wiring components.")]
    public static string Describe(
        RhinoDoc _,
        [Description("Component name as it appears in the component library (e.g. 'Number Slider', 'Addition'). Case-insensitive.")] string name)
    {
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

        var inputs = Array.Empty<ParamInfo>();
        var outputs = Array.Empty<ParamInfo>();
        string kind;

        switch (obj)
        {
            case IGH_Component comp:
                kind = "Component";
                inputs = comp.Params.Input.Select(ToInfo).ToArray();
                outputs = comp.Params.Output.Select(ToInfo).ToArray();
                break;
            case IGH_Param param:
                kind = "Param";
                inputs = [ToInfo(param)];
                break;
            default:
                kind = obj.GetType().Name;
                break;
        }

        var info = new ComponentInfo(
            obj.Name,
            obj.NickName,
            obj.Description,
            obj.Category,
            obj.SubCategory,
            kind,
            inputs,
            outputs);

        return JsonSerializer.Serialize(info);
    }

    private static ParamInfo ToInfo(IGH_Param p) => new(
        p.Name,
        p.NickName,
        p.Description,
        p.TypeName,
        p.Access.ToString(),
        p.Optional);
}
