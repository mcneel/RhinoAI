using System.Drawing;

using RhMcp.Resources;

using Grasshopper;
using Grasshopper.Kernel;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_PlaceComponentTool
{
    public record struct PlacedInfo(Guid Id, string Name, string Category, string SubCategory, float X, float Y);
    public record struct Candidate(Guid Guid, string Name, string Category, string SubCategory);
    public record struct AmbiguousResult(string Error, Candidate[] Candidates);

    [McpServerTool(Name = "place_component")]
    [Description("Place a Grasshopper component onto the active GH1 canvas. 'selector' may be a Guid (proxy id) or a component name. If multiple components share the name, returns an ambiguity payload listing candidates.")]
    public static string Place(
        RhinoDoc _,
        [Description("Component Guid (proxy id) or component Name (case-insensitive).")] string selector,
        [Description("Canvas X position in pixels.")] float x = 100,
        [Description("Canvas Y position in pixels.")] float y = 100,
        [Description("If true, trigger a new solution after placing. Set false to batch multiple operations and solve once at the end.")] bool solve = true)
    {
        if (!GH1_Utils.TryGetOrCreateDoc(out GH_Document doc))
            return "Could not get or create GH document";

        IGH_DocumentObject? obj;

        if (Guid.TryParse(selector, out Guid guid))
        {
            obj = Instances.ComponentServer.EmitObject(guid);
            if (obj is null) return $"No component with guid '{guid}' found";
        }
        else
        {
            var matches = new List<IGH_ObjectProxy>();
            foreach (IGH_ObjectProxy p in Instances.ComponentServer.ObjectProxies)
            {
                if (string.Equals(p.Desc.Name, selector, StringComparison.OrdinalIgnoreCase))
                    matches.Add(p);
            }

            if (matches.Count == 0) return $"No component named '{selector}' found";

            if (matches.Count > 1)
            {
                var candidates = matches
                    .Select(p => new Candidate(p.Guid, p.Desc.Name, p.Desc.Category, p.Desc.SubCategory))
                    .ToArray();
                return JsonSerializer.Serialize(new AmbiguousResult("ambiguous", candidates));
            }

            obj = matches[0].CreateInstance();
            if (obj is null) return $"Failed to instantiate '{selector}'";
        }

        if (obj.Attributes is null) obj.CreateAttributes();
        obj.Attributes.Pivot = new PointF(x, y);

        RhinoApp.InvokeAndWait(() =>
        {
            doc.AddObject(obj, false);
            if (solve) doc.NewSolution(false);
            GH1_Utils.ZoomExtents();
        });

        return JsonSerializer.Serialize(new PlacedInfo(
            obj.InstanceGuid,
            obj.Name,
            obj.Category,
            obj.SubCategory,
            x,
            y));
    }
}
