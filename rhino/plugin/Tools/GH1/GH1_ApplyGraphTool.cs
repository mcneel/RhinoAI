using System.Drawing;

using Rhino.AI.Resources;

using Grasshopper;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class GH1_ApplyGraphTool
{
    public record struct ComponentSpec(string Key, string Selector, int X, int Y);
    public record struct SliderSpec(string Key, decimal Min, decimal Value, decimal Max, string Type, string? Name, int X, int Y);
    public record struct WireSpec(string SrcKey, string Src, string DstKey, string Dst);

    public record struct PlacedRef(string Key, Guid Id, string Kind);
    public record struct PlaceError(string Key, string Error);
    public record struct WireResult(int Index, bool Ok, string? Error);

    public record struct ApplyResult(
        PlacedRef[] Placed,
        PlaceError[] PlaceErrors,
        WireResult[] Wires,
        int WiresOk,
        GH1_Utils.SolveSummary? Solve);

    [McpServerTool("g1_apply_graph", "Apply GH1 Graph", false, false)]
    [Description("Place sliders + components and wire them in one call. References between objects use caller-supplied 'key' strings; the tool returns the key→Guid map. Failures in any step do not abort the rest; results report per-step status. Wire src/dst use the same selector semantics as 'g1_connect'.")]
    public static IToolResult Apply(
        RhinoDoc rhDoc,
        [Description("Sliders to place: {Key, Min, Value, Max, Type, Name?, X, Y}. Type ∈ 'float'|'int'|'even'|'odd'.")] SliderSpec[] sliders,
        [Description("Components to place: {Key, Selector, X, Y}. Selector is a Guid (preferred — avoids name ambiguity) or component Name.")] ComponentSpec[] components,
        [Description("Wires to create: {SrcKey, Src, DstKey, Dst}. Keys must match a slider or component key above.")] WireSpec[] wires,
        [Description("If true, trigger a new solution at the end.")] bool solve = true,
        [Description("If true (the default), sources already wired into a destination input before this call are removed first. Wires added within this same call accumulate. Pass false to add alongside everything.")] bool replace = true,
        [Description("Also match obsolete/hidden components by name (a Guid always works). Default false.")] bool includeDeprecated = false)
    {
        if (!GH1_Utils.TryGetOrCreateDoc(rhDoc, out GH_Document doc))
            return GH1_Failures.NoDocument;

        Coercions coerced = new();
        Rewiring rewiring = new(replace);
        Dictionary<string, IGH_DocumentObject> keyToObj = new(StringComparer.Ordinal);
        List<PlacedRef> placed = [];
        List<PlaceError> placeErrors = [];
        WireResult[] wireResults = new WireResult[wires?.Length ?? 0];

        if (sliders is not null)
        {
            foreach (SliderSpec s in sliders)
            {
                if (keyToObj.ContainsKey(s.Key))
                {
                    placeErrors.Add(new PlaceError(s.Key, "duplicate key"));
                }
                else if (TryPlaceSlider(doc, s, coerced, out GH_NumberSlider? slider, out string err) && slider is not null)
                {
                    keyToObj[s.Key] = slider!;
                    placed.Add(new PlacedRef(s.Key, slider!.InstanceGuid, "Slider"));
                }
                else
                {
                    string errMessage = slider is null ? "Slider could not be found" : err;
                    placeErrors.Add(new PlaceError(s.Key, errMessage));
                }
            }
        }

        if (components is not null)
        {
            foreach (var c in components)
            {
                if (keyToObj.ContainsKey(c.Key))
                {
                    placeErrors.Add(new PlaceError(c.Key, "duplicate key"));
                }
                else if (TryPlaceComponent(doc, c, includeDeprecated, out var obj, out var err))
                {
                    keyToObj[c.Key] = obj!;
                    placed.Add(new PlacedRef(c.Key, obj!.InstanceGuid, GH1_Utils.ClassifyKind(obj.GetType())));
                }
                else
                {
                    placeErrors.Add(new PlaceError(c.Key, err));
                }
            }
        }

        if (wires is not null)
        {
            for (int i = 0; i < wires.Length; i++)
                wireResults[i] = WireOne(i, wires[i], keyToObj, rewiring);
        }

        GH1_Utils.SolveSummary? summary = null;
        if (solve)
        {
            doc.NewSolution(false);
            summary = GH1_Utils.Summarize(doc);
        }
        GH1_Utils.Redraw();

        int wiresOk = 0;
        for (int i = 0; i < wireResults.Length; i++)
        if (wireResults[i].Ok)
            wiresOk++;

        if (rewiring.Guidance is string rewired)
            coerced.Note(rewired);

        return Success(new ApplyResult(placed.ToArray(), placeErrors.ToArray(), wireResults, wiresOk, summary), coerced.Guidance);
    }

    private static bool TryPlaceSlider(GH_Document doc, SliderSpec s, Coercions coerced, out GH_NumberSlider? slider, out string error)
    {
        slider = null;
        if (!TryParseAccuracy(s.Type, out GH_SliderAccuracy accuracy))
            coerced.Note($"slider '{s.Key}' type '{s.Type}' is not one of 'float', 'int', 'even', 'odd', so 'float' was used");

        (decimal min, decimal value, decimal max) = coerced.SliderRange(s.Min, s.Value, s.Max, $"slider '{s.Key}'");

        slider = new GH_NumberSlider();
        slider.CreateAttributes();
        slider.Slider.Minimum = min;
        slider.Slider.Maximum = max;
        slider.Slider.Value = value;
        slider.Slider.Type = accuracy;
        if (!string.IsNullOrEmpty(s.Name))
            slider.NickName = s.Name;
        slider.Attributes.Pivot = new PointF(s.X, s.Y);
        doc.AddObject(slider, false);
        error = "";
        return true;
    }

    private static bool TryPlaceComponent(GH_Document doc, ComponentSpec c, bool includeDeprecated, out IGH_DocumentObject? obj, out string error)
    {
        obj = null;
        if (Guid.TryParse(c.Selector, out Guid guid))
        {
            obj = Grasshopper.Instances.ComponentServer.EmitObject(guid);
            if (obj is null)
            { error = $"No component with guid '{guid}'"; return false; }
        }
        else
        {
            switch (GH1_ProxyResolver.Resolve(c.Selector, includeDeprecated))
            {
                case GH1_ProxyResolution.Found found:
                    obj = found.Proxy.CreateInstance();
                    if (obj is null)
                    { error = $"Failed to instantiate '{c.Selector}'"; return false; }
                    break;
                case GH1_ProxyResolution.Ambiguous ambiguous:
                    error = $"Component name '{c.Selector}' is ambiguous ({ambiguous.Candidates.Count} matches): {Summarize(ambiguous.Candidates)}. Pass a Guid to disambiguate.";
                    return false;
                case GH1_ProxyResolution.OnlyDeprecated onlyDeprecated:
                    error = $"Only obsolete or hidden components match '{c.Selector}': {Summarize(onlyDeprecated.Candidates)}. Pass a Guid, or set includeDeprecated=true, to use one.";
                    return false;
                case GH1_ProxyResolution.NotFound:
                    error = $"No component named '{c.Selector}'";
                    return false;
                default:
                    throw new InvalidOperationException("Unhandled resolution case");
            }
        }
        if (obj.Attributes is null)
            obj.CreateAttributes();
        if (obj.Attributes is null)
        { error = $"Failed to create attributes for '{c.Selector}'"; return false; }
        obj.Attributes.Pivot = new PointF(c.X, c.Y);
        doc.AddObject(obj, false);
        error = "";
        return true;
    }

    private static string Summarize(IReadOnlyList<IGH_ObjectProxy> proxies) =>
        string.Join(", ", proxies.Select(p => $"{p.Guid} ({p.Desc.Category}/{p.Desc.SubCategory})"));

    private static WireResult WireOne(int idx, WireSpec w, Dictionary<string, IGH_DocumentObject> keyToObj, Rewiring rewiring)
    {
        if (!keyToObj.TryGetValue(w.SrcKey, out var srcObj))
            return new WireResult(idx, false, $"src_key '{w.SrcKey}' did not match a placed object");
        if (!keyToObj.TryGetValue(w.DstKey, out var dstObj))
            return new WireResult(idx, false, $"dst_key '{w.DstKey}' did not match a placed object");

        if (GH1_GraphOps.WouldCycle(srcObj, dstObj))
            return new WireResult(idx, false, $"wiring key '{w.SrcKey}' into '{w.DstKey}' would create a cycle");

        if (!GH1_GraphOps.TryResolveOutput(srcObj, w.Src, out IGH_Param? srcParam, out string srcErr))
            return new WireResult(idx, false, srcErr);
        if (!GH1_GraphOps.TryResolveInput(dstObj, w.Dst, out IGH_Param? dstParam, out string dstErr))
            return new WireResult(idx, false, dstErr);

        try
        {
            rewiring.Note(GH1_GraphOps.Connect(srcParam!, dstParam!, rewiring.ShouldClear(dstParam!.InstanceGuid)));
        }
        catch (Exception ex)
        {
            return new WireResult(idx, false, ex.Message);
        }

        return new WireResult(idx, true, null);
    }

    private static bool TryParseAccuracy(string type, out GH_SliderAccuracy accuracy)
    {
        switch (type?.ToLowerInvariant())
        {
            case "float":
                accuracy = GH_SliderAccuracy.Float;
                return true;
            case "int":
                accuracy = GH_SliderAccuracy.Integer;
                return true;
            case "even":
                accuracy = GH_SliderAccuracy.Even;
                return true;
            case "odd":
                accuracy = GH_SliderAccuracy.Odd;
                return true;
            default:
                accuracy = GH_SliderAccuracy.Float;
                return false;
        }
    }
}
