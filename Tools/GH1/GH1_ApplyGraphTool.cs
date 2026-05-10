using System.Drawing;

using RhMcp.Resources;

using Grasshopper;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_ApplyGraphTool
{
    public record struct ComponentSpec(string Key, string Selector, float X, float Y);
    public record struct SliderSpec(string Key, double Min, double Value, double Max, string Type, string? Name, float X, float Y);
    public record struct WireSpec(string SrcKey, string Src, string DstKey, string Dst);

    public record struct PlacedRef(string Key, Guid Id, string Kind);
    public record struct PlaceError(string Key, string Error);
    public record struct WireResult(int Index, bool Ok, string? Error);

    public record struct ApplyResult(
        PlacedRef[] Placed,
        PlaceError[] PlaceErrors,
        WireResult[] Wires,
        int WiresOk);

    [McpServerTool(Name = "apply_graph")]
    [Description("Place sliders + components and wire them in one call. References between objects use caller-supplied 'key' strings; the tool returns the key→Guid map. Failures in any step do not abort the rest; results report per-step status. Wire src/dst use the same selector semantics as 'connect'.")]
    public static string Apply(
        RhinoDoc _,
        [Description("Sliders to place: {Key, Min, Value, Max, Type, Name?, X, Y}. Type ∈ 'float'|'int'|'even'|'odd'.")] SliderSpec[] sliders,
        [Description("Components to place: {Key, Selector, X, Y}. Selector is a Guid (preferred — avoids name ambiguity) or component Name.")] ComponentSpec[] components,
        [Description("Wires to create: {SrcKey, Src, DstKey, Dst}. Keys must match a slider or component key above.")] WireSpec[] wires,
        [Description("If true, trigger a new solution at the end.")] bool solve = true)
    {
        if (!GH1_Utils.TryGetOrCreateDoc(out GH_Document doc))
            return "Could not get or create GH document";

        var keyToObj = new Dictionary<string, IGH_DocumentObject>(StringComparer.Ordinal);
        var placed = new List<PlacedRef>();
        var placeErrors = new List<PlaceError>();
        var wireResults = new WireResult[wires?.Length ?? 0];

        RhinoApp.InvokeAndWait(() =>
        {
            if (sliders is not null)
            {
                foreach (var s in sliders)
                {
                    if (TryPlaceSlider(doc, s, out var slider, out var err))
                    {
                        keyToObj[s.Key] = slider!;
                        placed.Add(new PlacedRef(s.Key, slider!.InstanceGuid, "Slider"));
                    }
                    else
                    {
                        placeErrors.Add(new PlaceError(s.Key, err));
                    }
                }
            }

            if (components is not null)
            {
                foreach (var c in components)
                {
                    if (TryPlaceComponent(doc, c, out var obj, out var err))
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
                    wireResults[i] = WireOne(i, wires[i], keyToObj);
            }

            if (solve) doc.NewSolution(false);
            GH1_Utils.Redraw();
        });

        int wiresOk = 0;
        for (int i = 0; i < wireResults.Length; i++) if (wireResults[i].Ok) wiresOk++;

        return JsonSerializer.Serialize(new ApplyResult(
            placed.ToArray(),
            placeErrors.ToArray(),
            wireResults,
            wiresOk));
    }

    private static bool TryPlaceSlider(GH_Document doc, SliderSpec s, out GH_NumberSlider? slider, out string error)
    {
        slider = null;
        if (!TryParseAccuracy(s.Type, out GH_SliderAccuracy accuracy))
        {
            error = $"Invalid slider type '{s.Type}'. Valid: 'float', 'int', 'even', 'odd'.";
            return false;
        }
        slider = new GH_NumberSlider();
        slider.CreateAttributes();
        slider.Slider.Minimum = (decimal)s.Min;
        slider.Slider.Maximum = (decimal)s.Max;
        slider.Slider.Value = (decimal)s.Value;
        slider.Slider.Type = accuracy;
        if (!string.IsNullOrEmpty(s.Name)) slider.NickName = s.Name;
        slider.Attributes.Pivot = new PointF(s.X, s.Y);
        doc.AddObject(slider, false);
        error = "";
        return true;
    }

    private static bool TryPlaceComponent(GH_Document doc, ComponentSpec c, out IGH_DocumentObject? obj, out string error)
    {
        obj = null;
        if (Guid.TryParse(c.Selector, out Guid guid))
        {
            obj = Instances.ComponentServer.EmitObject(guid);
            if (obj is null) { error = $"No component with guid '{guid}'"; return false; }
        }
        else
        {
            var matches = new List<IGH_ObjectProxy>();
            foreach (IGH_ObjectProxy p in Instances.ComponentServer.ObjectProxies)
                if (string.Equals(p.Desc.Name, c.Selector, StringComparison.OrdinalIgnoreCase))
                    matches.Add(p);

            if (matches.Count == 0) { error = $"No component named '{c.Selector}'"; return false; }
            if (matches.Count > 1)
            {
                var names = string.Join(", ", matches.Select(p => $"{p.Guid} ({p.Desc.Category}/{p.Desc.SubCategory})"));
                error = $"Component name '{c.Selector}' is ambiguous ({matches.Count} matches): {names}. Pass a Guid to disambiguate.";
                return false;
            }
            obj = matches[0].CreateInstance();
            if (obj is null) { error = $"Failed to instantiate '{c.Selector}'"; return false; }
        }
        if (obj.Attributes is null) obj.CreateAttributes();
        obj.Attributes.Pivot = new PointF(c.X, c.Y);
        doc.AddObject(obj, false);
        error = "";
        return true;
    }

    private static WireResult WireOne(int idx, WireSpec w, Dictionary<string, IGH_DocumentObject> keyToObj)
    {
        if (!keyToObj.TryGetValue(w.SrcKey, out var srcObj))
            return new WireResult(idx, false, $"src_key '{w.SrcKey}' did not match a placed object");
        if (!keyToObj.TryGetValue(w.DstKey, out var dstObj))
            return new WireResult(idx, false, $"dst_key '{w.DstKey}' did not match a placed object");

        if (!GH1_GraphOps.TryResolveOutput(srcObj, w.Src, out IGH_Param? srcParam, out string srcErr))
            return new WireResult(idx, false, srcErr);
        if (!GH1_GraphOps.TryResolveInput(dstObj, w.Dst, out IGH_Param? dstParam, out string dstErr))
            return new WireResult(idx, false, dstErr);

        try
        {
            if (!dstParam!.Sources.Contains(srcParam)) dstParam!.AddSource(srcParam);
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
            case "float": accuracy = GH_SliderAccuracy.Float; return true;
            case "int": accuracy = GH_SliderAccuracy.Integer; return true;
            case "even": accuracy = GH_SliderAccuracy.Even; return true;
            case "odd": accuracy = GH_SliderAccuracy.Odd; return true;
            default: accuracy = GH_SliderAccuracy.Float; return false;
        }
    }
}
