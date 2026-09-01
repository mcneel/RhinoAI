using System.Drawing;

using RhinoAI.Resources;

using Grasshopper;
using Grasshopper.Kernel;

namespace RhinoAI.Tools;

[McpServerToolType]
public static class GH1_PlaceComponentTool
{
    public record struct PlacedInfo(Guid Id, string Name, string Category, string SubCategory, float X, float Y);

    [McpServerTool("g1_place_component", "Place GH1 Component", false, false)]
    [Description("Place a Grasshopper component onto the active GH1 canvas. 'selector' may be a Guid (proxy id) or a component name. Matches obsolete/hidden by name only when includeDeprecated is true (a Guid always works); ambiguous names return candidates.")]
    public static string Place(
        RhinoDoc rhDoc,
        [Description("Component Guid (proxy id) or component Name (case-insensitive).")] string selector,
        [Description("Canvas X position in pixels.")] float x = 100,
        [Description("Canvas Y position in pixels.")] float y = 100,
        [Description("If true, trigger a new solution after placing. Set false to batch multiple operations and solve once at the end.")] bool solve = true,
        [Description("Also match obsolete/hidden components by name (a Guid always works). Default false.")] bool includeDeprecated = false)
    {
        if (!GH1_Utils.TryGetOrCreateDoc(rhDoc, out GH_Document doc))
            return "Could not get or create GH document";

        if (Guid.TryParse(selector, out Guid guid))
        {
            IGH_DocumentObject? emitted = Instances.ComponentServer.EmitObject(guid);
            if (emitted is null) return $"No component with guid '{guid}' found";
            return PlaceObject(doc, emitted, selector, x, y, solve);
        }

        return GH1_ProxyResolver.Resolve(selector, includeDeprecated) switch
        {
            GH1_ProxyResolution.Found found => PlaceResolved(doc, found.Proxy, selector, x, y, solve),
            GH1_ProxyResolution.Ambiguous ambiguous => JsonSerializer.Serialize(new GH1_UnresolvedResult("ambiguous", GH1_ProxyResolver.AmbiguousMessage, GH1_ProxyResolver.ToCandidates(ambiguous.Candidates))),
            GH1_ProxyResolution.OnlyDeprecated onlyDeprecated => JsonSerializer.Serialize(new GH1_UnresolvedResult("only_deprecated", GH1_ProxyResolver.OnlyDeprecatedMessage, GH1_ProxyResolver.ToCandidates(onlyDeprecated.Candidates))),
            GH1_ProxyResolution.NotFound => $"No component named '{selector}' found",
            _ => throw new InvalidOperationException("Unhandled resolution case"),
        };
    }

    private static string PlaceResolved(GH_Document doc, IGH_ObjectProxy proxy, string selector, float x, float y, bool solve)
    {
        IGH_DocumentObject? obj = proxy.CreateInstance();
        if (obj is null) return $"Failed to instantiate '{selector}'";
        return PlaceObject(doc, obj, selector, x, y, solve);
    }

    private static string PlaceObject(GH_Document doc, IGH_DocumentObject obj, string selector, float x, float y, bool solve)
    {
        if (obj.Attributes is null) obj.CreateAttributes();
        if (obj.Attributes is null) return $"Failed to create attributes for '{selector}'";
        obj.Attributes.Pivot = new PointF(x, y);

        doc.AddObject(obj, false);
        if (solve) doc.NewSolution(false);
        GH1_Utils.ZoomExtents();

        return JsonSerializer.Serialize(new PlacedInfo(
            obj.InstanceGuid,
            obj.Name,
            obj.Category,
            obj.SubCategory,
            x,
            y));
    }
}
