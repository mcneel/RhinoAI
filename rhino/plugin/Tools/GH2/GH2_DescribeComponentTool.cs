using RhMcp.Resources;

using Grasshopper2.Components;
using Grasshopper2.Doc;
using Grasshopper2.Framework;
using Grasshopper2.Parameters;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_DescribeComponentTool
{
    public record struct ParamInfo(string Name, string UserName, string Description, string TypeName, string Access, string Requirement);

    public record struct ComponentInfo(
        string Name,
        string UserName,
        string Description,
        string Category,
        string SubCategory,
        string Kind,
        ParamInfo[] Inputs,
        ParamInfo[] Outputs);

    [McpServerTool(Name = "describe_component")]
    [Description("Look up a GH2 component by name and return its chapter, info, and input/output parameter list. Useful before placing or wiring components.")]
    public static string Describe(
        RhinoDoc _,
        [Description("Component name as it appears in the component library (e.g. 'Slider', 'Addition'). Case-insensitive.")] string name)
    {
        ObjectProxy? proxy = null;
        foreach (var p in ObjectProxies.Proxies)
        {
            if (string.Equals(p.Nomen.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                proxy = p;
                break;
            }
        }
        if (proxy is null) return $"No component named '{name}' found";

        var obj = proxy.Emit();
        if (obj is null) return $"Failed to instantiate '{name}'";

        var inputs = Array.Empty<ParamInfo>();
        var outputs = Array.Empty<ParamInfo>();
        string kind;

        switch (obj)
        {
            case Component comp:
                kind = "Component";
                inputs = comp.Parameters.Inputs.Select(ToInfo).ToArray();
                outputs = comp.Parameters.Outputs.Select(ToInfo).ToArray();
                break;
            case IParameter param:
                kind = "Param";
                inputs = [ToInfo(param)];
                break;
            default:
                kind = obj.GetType().Name;
                break;
        }

        var info = new ComponentInfo(
            obj.Nomen.Name,
            obj.UserName ?? "",
            obj.Nomen.Info,
            obj.Nomen.Chapter,
            obj.Nomen.Section,
            kind,
            inputs,
            outputs);

        return JsonSerializer.Serialize(info);
    }

    private static ParamInfo ToInfo(IParameter p) => new(
        p.Nomen.Name,
        p.UserName ?? "",
        p.Nomen.Info,
        p.TypeAssistantWeak?.Name ?? p.TypeAssistantWeak?.Type.Name ?? "",
        p.Access.ToString(),
        p.Requirement.ToString());
}
