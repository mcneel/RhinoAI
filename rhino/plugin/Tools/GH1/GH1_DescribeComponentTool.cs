using RhinoAI.Resources;

using Grasshopper;
using Grasshopper.Kernel;

namespace RhinoAI.Tools;

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

    [McpServerTool("g1_describe_component", "Describe GH1 Component", true, false)]
    [Description("Look up a Grasshopper component by name and return its category, description, and input/output parameter list. Useful before placing or wiring components. Ignores obsolete/hidden unless includeDeprecated.")]
    public static string Describe(
        RhinoDoc _,
        [Description("Component name as it appears in the component library (e.g. 'Number Slider', 'Addition'). Case-insensitive.")] string name,
        [Description("Include obsolete/hidden components (e.g. legacy scripting). Default false.")] bool includeDeprecated = false) =>
        GH1_ProxyResolver.Resolve(name, includeDeprecated) switch
        {
            GH1_ProxyResolution.Found found => DescribeProxy(found.Proxy),
            GH1_ProxyResolution.Ambiguous ambiguous => JsonSerializer.Serialize(new GH1_UnresolvedResult("ambiguous", GH1_ProxyResolver.AmbiguousMessage, GH1_ProxyResolver.ToCandidates(ambiguous.Candidates))),
            GH1_ProxyResolution.OnlyDeprecated onlyDeprecated => JsonSerializer.Serialize(new GH1_UnresolvedResult("only_deprecated", GH1_ProxyResolver.OnlyDeprecatedMessage, GH1_ProxyResolver.ToCandidates(onlyDeprecated.Candidates))),
            GH1_ProxyResolution.NotFound => $"No component named '{name}' found",
            _ => throw new InvalidOperationException("Unhandled resolution case"),
        };

    private static string DescribeProxy(IGH_ObjectProxy proxy)
    {
        IGH_DocumentObject obj = proxy.CreateInstance();
        if (obj is null) return $"Failed to instantiate '{proxy.Desc.Name}'";

        (string kind, ParamInfo[] inputs, ParamInfo[] outputs) = obj switch
        {
            IGH_Component comp => ("Component", comp.Params.Input.Select(ToInfo).ToArray(), comp.Params.Output.Select(ToInfo).ToArray()),
            IGH_Param param => ("Param", [ToInfo(param)], []),
            _ => (obj.GetType().Name, [], []),
        };

        ComponentInfo info = new(
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
