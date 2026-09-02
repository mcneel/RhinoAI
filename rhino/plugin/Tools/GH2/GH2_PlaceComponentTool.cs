using RhinoAI.Resources;

using Eto.Drawing;

using Grasshopper2.Doc;
using Grasshopper2.Framework;

namespace RhinoAI.Tools;

[McpServerToolType]
public static class GH2_PlaceComponentTool
{
    public record struct PlacedInfo(Guid Id, string Name, string Category, string SubCategory, float X, float Y);
    public record struct ErrResult(bool Ok, string Error);

    [McpServerTool("g2_place_component", "Place GH2 Component", false, false)]
    [Description("Place a GH2 component onto the active canvas. 'selector' may be a Guid (proxy id) or a component name. Matches obsolete/hidden by name only when includeDeprecated is true (a Guid always works); ambiguous names return candidates.")]
    public static string Place(
        RhinoDoc rhDoc,
        [Description("Component Guid (proxy id) or component Name (case-insensitive).")] string selector,
        [Description("Canvas X position in pixels.")] float x = 100,
        [Description("Canvas Y position in pixels.")] float y = 100,
        [Description("If true, trigger a new solution after placing. Set false to batch multiple operations and solve once at the end.")] bool solve = true,
        [Description("Also match obsolete/hidden components by name (a Guid always works). Default false.")] bool includeDeprecated = false)
    {
        if (!GH2_Utils.TryGetDoc(rhDoc, out Document doc))
            return Err("Could not get or create GH2 document");

        if (Guid.TryParse(selector, out Guid guid))
        {
            ObjectProxy? proxy = ObjectProxies.FindById(guid);
            if (proxy is null) return Err($"No component with guid '{guid}' found");
            IDocumentObject? emitted = proxy.Emit();
            if (emitted is null) return Err($"Failed to emit object for guid '{guid}'");
            return PlaceObject(doc, emitted, x, y, solve);
        }

        return GH2_ProxyResolver.Resolve(selector, includeDeprecated) switch
        {
            GH2_ProxyResolution.Found found => PlaceResolved(doc, found.Proxy, selector, x, y, solve),
            GH2_ProxyResolution.Ambiguous ambiguous => JsonSerializer.Serialize(new GH2_UnresolvedResult("ambiguous", GH2_ProxyResolver.AmbiguousMessage, GH2_ProxyResolver.ToCandidates(ambiguous.Candidates))),
            GH2_ProxyResolution.OnlyDeprecated onlyDeprecated => JsonSerializer.Serialize(new GH2_UnresolvedResult("only_deprecated", GH2_ProxyResolver.OnlyDeprecatedMessage, GH2_ProxyResolver.ToCandidates(onlyDeprecated.Candidates))),
            GH2_ProxyResolution.NotFound => Err($"No component named '{selector}' found"),
            _ => throw new InvalidOperationException("Unhandled resolution case"),
        };
    }

    private static string PlaceResolved(Document doc, ObjectProxy proxy, string selector, float x, float y, bool solve)
    {
        IDocumentObject? obj = proxy.Emit();
        if (obj is null) return Err($"Failed to instantiate '{selector}'");
        return PlaceObject(doc, obj, x, y, solve);
    }

    private static string PlaceObject(Document doc, IDocumentObject obj, float x, float y, bool solve)
    {
        doc.Objects.Add(obj, new PointF(x, y));
        if (solve) doc.Solution.Start();
        GH2_Utils.Redraw();

        return JsonSerializer.Serialize(new PlacedInfo(
            obj.InstanceId,
            obj.Nomen.Name,
            obj.Nomen.Chapter,
            obj.Nomen.Section,
            x,
            y));
    }

    private static string Err(string msg) => JsonSerializer.Serialize(new ErrResult(false, msg));
}
