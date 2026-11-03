using System.Drawing;

using Rhino.AI.Resources;

using Grasshopper;
using Grasshopper.Kernel;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class GH1_PlaceComponentTool
{
    public record struct PlacedInfo(Guid Id, string Name, string Category, string SubCategory, int X, int Y);

    [McpServerTool("g1_place_component", "Place GH1 Component", false, false)]
    [Description("Place a Grasshopper component onto the active GH1 canvas. 'selector' may be a Guid (proxy id) or a component name. Matches obsolete/hidden by name only when includeDeprecated is true (a Guid always works); ambiguous names return candidates.")]
    public static IToolResult Place(
        RhinoDoc rhDoc,
        [Description("Component Guid (proxy id) or component Name (case-insensitive).")] string selector,
        [Description("Canvas X position in pixels.")] int x = 100,
        [Description("Canvas Y position in pixels.")] int y = 100,
        [Description("If true, trigger a new solution after placing. Set false to batch multiple operations and solve once at the end.")] bool solve = true,
        [Description("Also match obsolete/hidden components by name (a Guid always works). Default false.")] bool includeDeprecated = false)
    {
        if (!GH1_Utils.TryGetOrCreateDoc(rhDoc, out GH_Document doc))
            return GH1_Failures.NoDocument;

        if (Guid.TryParse(selector, out Guid guid))
        {
            IGH_DocumentObject? emitted = Grasshopper.Instances.ComponentServer.EmitObject(guid);
            if (emitted is null)
                return Failure(ToolError.GH_Component_NotFound, $"No component with guid '{guid}' found");
            return PlaceObject(doc, emitted, selector, x, y, solve);
        }

        return GH1_ProxyResolver.Resolve(selector, includeDeprecated) switch
        {
            GH1_ProxyResolution.Found found => PlaceResolved(doc, found.Proxy, selector, x, y, solve),
            GH1_ProxyResolution.Ambiguous ambiguous => Failure(
                ToolError.Ambiguous,
                ContentBlock.CreateJson(GH1_ProxyResolver.ToCandidates(ambiguous.Candidates)),
                $"'{selector}' matches {ambiguous.Candidates.Count} components",
                GH1_ProxyResolver.AmbiguousMessage),
            GH1_ProxyResolution.OnlyDeprecated onlyDeprecated => Failure(
                ToolError.GH_Component_NotFound,
                ContentBlock.CreateJson(GH1_ProxyResolver.ToCandidates(onlyDeprecated.Candidates)),
                $"Only obsolete or hidden components match '{selector}'",
                GH1_ProxyResolver.OnlyDeprecatedMessage),
            GH1_ProxyResolution.NotFound => Failure(ToolError.GH_Component_NotFound, $"No component named '{selector}' found", "Find the right name with g1_search_components"),
            _ => throw new InvalidOperationException("Unhandled resolution case"),
        };
    }

    private static IToolResult PlaceResolved(GH_Document doc, IGH_ObjectProxy proxy, string selector, int x, int y, bool solve)
    {
        IGH_DocumentObject? obj = proxy.CreateInstance();
        if (obj is null)
            return Failure(ToolError.GH_Component_Failed, $"Failed to instantiate '{selector}'");
        return PlaceObject(doc, obj, selector, x, y, solve);
    }

    private static IToolResult PlaceObject(GH_Document doc, IGH_DocumentObject obj, string selector, int x, int y, bool solve)
    {
        if (obj.Attributes is null)
            obj.CreateAttributes();

        if (obj.Attributes is null)
            return Failure(ToolError.GH_Component_Failed, $"Failed to create attributes for '{selector}'");
        obj.Attributes.Pivot = new PointF(x, y);

        doc.AddObject(obj, false);
        if (solve) doc.NewSolution(false);
        GH1_Utils.ZoomExtents();

        return Success(new PlacedInfo(
            obj.InstanceGuid,
            obj.Name,
            obj.Category,
            obj.SubCategory,
            x,
            y));
    }
}
