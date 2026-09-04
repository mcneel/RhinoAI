using Rhino.DocObjects;

namespace RhinoAI.WebPanel;

// What the composer's @ menu can attach to a prompt: the things in the document a user would point
// at. Read straight off the document each time rather than cached, because the interesting one is
// the selection and it changes constantly.
//
// Must run on the UI thread; every caller is already there.
internal static class PanelContextSource
{
    // Enough to choose from without turning the menu into a layer manager. The menu filters as you
    // type, so a big document is still reachable by name.
    private const int MaxLayers = 12;

    public static List<PanelContextItem> For(RhinoDoc doc)
    {
        List<PanelContextItem> items = new();

        RhinoObject[] selected = doc.Objects.GetSelectedObjects(includeLights: false, includeGrips: false).ToArray();
        if (selected.Length > 0)
            items.Add(new PanelContextItem("ctx-selection", "selection", "Selection", Describe(selected), selected.Length));

        if (doc.Views.ActiveView is { } view)
            items.Add(new PanelContextItem("ctx-view", "view", view.ActiveViewport.Name, "active viewport", null));

        // Labelled by what it attaches rather than by the file, which reads as a bare "Untitled" on
        // an unsaved document. The file name belongs in the detail line, alongside the counts.
        Dictionary<int, int> perLayer = CountByLayer(doc);
        string name = string.IsNullOrEmpty(doc.Path) ? "unsaved" : System.IO.Path.GetFileName(doc.Path);
        items.Add(new PanelContextItem(
            "ctx-document",
            "document",
            "Whole document",
            $"{name} · {perLayer.Values.Sum()} objects · {perLayer.Count} layers in use",
            null));

        // Busiest first: an empty layer is rarely what someone means by "@".
        foreach (Layer layer in doc.Layers)
        {
            if (layer.IsDeleted)
                continue;
            if (!perLayer.TryGetValue(layer.Index, out int count) || count == 0)
                continue;
            items.Add(new PanelContextItem($"ctx-layer-{layer.Index}", "layer", layer.FullPath, null, count));
        }

        return items
            .Take(items.Count(i => i.Kind != "layer") + MaxLayers)
            .ToList();
    }

    // One pass rather than a FindByLayer per layer, which would re-scan the table each time.
    private static Dictionary<int, int> CountByLayer(RhinoDoc doc)
    {
        Dictionary<int, int> counts = new();
        foreach (RhinoObject obj in doc.Objects)
        {
            int index = obj.Attributes.LayerIndex;
            counts[index] = counts.TryGetValue(index, out int current) ? current + 1 : 1;
        }
        return counts;
    }

    private static string Describe(IReadOnlyList<RhinoObject> selected)
    {
        IEnumerable<string> parts = selected
            .GroupBy(o => o.ObjectType)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}{(g.Count() == 1 ? string.Empty : "s")}");
        return string.Join(", ", parts);
    }
}
