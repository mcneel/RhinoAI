using RhMcp.Resources;

using Grasshopper2.Doc;
using Grasshopper2.Framework;
using Grasshopper2.Parameters;

using GH2Component = Grasshopper2.Components.Component;

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

    [McpServerTool("g2_describe_component", "Describe GH2 Component", true, false)]
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

        // Kind comes from the canonical classifier so g2_describe_component and
        // g2_search_components agree (a NumberSliderObject is "Slider", not "Param").
        // The switch only decides how to populate Inputs/Outputs.
        string kind = GH2_Utils.ClassifyKind(obj.GetType());

        ParamInfo[] inputs = Array.Empty<ParamInfo>();
        ParamInfo[] outputs = Array.Empty<ParamInfo>();

        switch (obj)
        {
            case GH2Component comp:
                inputs = comp.Parameters.Inputs.Select(ToInfo).ToArray();
                outputs = comp.Parameters.Outputs.Select(ToInfo).ToArray();
                break;
            case IParameter param:
                inputs = [ToInfo(param)];
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
