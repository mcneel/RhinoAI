using RhMcp.Resources;

using Eto.Drawing;

using Grasshopper2.Doc;
using Grasshopper2.Framework;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_PlaceComponentTool
{
    public record struct PlacedInfo(Guid Id, string Name, string Category, string SubCategory, float X, float Y);
    public record struct Candidate(Guid Guid, string Name, string Category, string SubCategory);
    public record struct AmbiguousResult(string Error, Candidate[] Candidates);
    public record struct ErrResult(bool Ok, string Error);

    [McpServerTool("g2_place_component", "Place GH2 Component", false, false)]
    [Description("Place a GH2 component onto the active canvas. 'selector' may be a Guid (proxy id) or a component name. If multiple components share the name, returns an ambiguity payload listing candidates.")]
    public static string Place(
        RhinoDoc rhDoc,
        [Description("Component Guid (proxy id) or component Name (case-insensitive).")] string selector,
        [Description("Canvas X position in pixels.")] float x = 100,
        [Description("Canvas Y position in pixels.")] float y = 100,
        [Description("If true, trigger a new solution after placing. Set false to batch multiple operations and solve once at the end.")] bool solve = true)
    {
        if (!GH2_Utils.TryGetDoc(rhDoc, out Document doc))
            return Err("Could not get or create GH2 document");

        IDocumentObject? obj;

        if (Guid.TryParse(selector, out Guid guid))
        {
            var proxy = ObjectProxies.FindById(guid);
            if (proxy is null) return Err($"No component with guid '{guid}' found");
            obj = proxy.Emit();
            if (obj is null) return Err($"Failed to emit object for guid '{guid}'");
        }
        else
        {
            var matches = new List<ObjectProxy>();
            foreach (var p in ObjectProxies.Proxies)
            {
                if (string.Equals(p.Nomen.Name, selector, StringComparison.OrdinalIgnoreCase))
                    matches.Add(p);
            }

            if (matches.Count == 0) return Err($"No component named '{selector}' found");

            if (matches.Count > 1)
            {
                var candidates = matches
                    .Select(p => new Candidate(p.Id, p.Nomen.Name, p.Nomen.Chapter, p.Nomen.Section))
                    .ToArray();
                return JsonSerializer.Serialize(new AmbiguousResult("ambiguous", candidates));
            }

            obj = matches[0].Emit();
            if (obj is null) return Err($"Failed to instantiate '{selector}'");
        }

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
