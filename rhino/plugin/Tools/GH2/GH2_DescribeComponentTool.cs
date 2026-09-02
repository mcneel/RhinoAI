using RhinoAI.Resources;

using Grasshopper2.Doc;
using Grasshopper2.Framework;
using Grasshopper2.Parameters;

using GH2Component = Grasshopper2.Components.Component;

namespace RhinoAI.Tools;

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
    [Description("Look up a GH2 component by name and return its chapter, info, and input/output parameter list. Useful before placing or wiring components. Ignores obsolete/hidden unless includeDeprecated.")]
    public static string Describe(
        RhinoDoc _,
        [Description("Component name as it appears in the component library (e.g. 'Slider', 'Addition'). Case-insensitive.")] string name,
        [Description("Include obsolete/hidden components (e.g. legacy scripting). Default false.")] bool includeDeprecated = false) =>
        GH2_ProxyResolver.Resolve(name, includeDeprecated) switch
        {
            GH2_ProxyResolution.Found found => DescribeProxy(found.Proxy),
            GH2_ProxyResolution.Ambiguous ambiguous => JsonSerializer.Serialize(new GH2_UnresolvedResult("ambiguous", GH2_ProxyResolver.AmbiguousMessage, GH2_ProxyResolver.ToCandidates(ambiguous.Candidates))),
            GH2_ProxyResolution.OnlyDeprecated onlyDeprecated => JsonSerializer.Serialize(new GH2_UnresolvedResult("only_deprecated", GH2_ProxyResolver.OnlyDeprecatedMessage, GH2_ProxyResolver.ToCandidates(onlyDeprecated.Candidates))),
            GH2_ProxyResolution.NotFound => $"No component named '{name}' found",
            _ => throw new InvalidOperationException("Unhandled resolution case"),
        };

    private static string DescribeProxy(ObjectProxy proxy)
    {
        IDocumentObject? obj = proxy.Emit();
        if (obj is null) return $"Failed to instantiate '{proxy.Nomen.Name}'";

        // Kind comes from the canonical classifier so describe and search agree (a NumberSliderObject is "Slider"); the switch only fills Inputs/Outputs.
        string kind = GH2_Utils.ClassifyKind(obj.GetType());

        (ParamInfo[] inputs, ParamInfo[] outputs) = obj switch
        {
            GH2Component comp => (comp.Parameters.Inputs.Select(ToInfo).ToArray(), comp.Parameters.Outputs.Select(ToInfo).ToArray()),
            IParameter param => ([ToInfo(param)], []),
            _ => ([], []),
        };

        ComponentInfo info = new(
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
