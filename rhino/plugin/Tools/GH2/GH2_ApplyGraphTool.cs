using RhMcp.Resources;

using Eto.Drawing;

using Grasshopper2.Doc;
using Grasshopper2.Framework;
using Grasshopper2.Parameters;
using Grasshopper2.Parameters.Special;
using Grasshopper2.UI;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_ApplyGraphTool
{
    public record struct ComponentSpec(string Key, string Selector, float X, float Y);
    public record struct SliderSpec(string Key, double Min, double Value, double Max, int Decimals, string? Name, float X, float Y);
    public record struct WireSpec(string SrcKey, string Src, string DstKey, string Dst);

    public record struct PlacedRef(string Key, Guid Id, string Kind);
    public record struct PlaceError(string Key, string Error);
    public record struct WireResult(int Index, bool Ok, string? Error);
    public record struct ErrResult(bool Ok, string Error);

    public record struct ApplyResult(
        PlacedRef[] Placed,
        PlaceError[] PlaceErrors,
        WireResult[] Wires,
        int WiresOk,
        bool Solved,
        string Phase,
        int Errors,
        int Warnings,
        GH2Diagnostic[] Diagnostics);

    [McpServerTool("g2_apply_graph", "Apply GH2 Graph", false, false)]
    [Description("Place sliders + components and wire them in one call on the active GH2 canvas. References between objects use caller-supplied 'key' strings; the tool returns the key→Guid map. Failures in any step do not abort the rest; results report per-step status. Wire src/dst use the same selector semantics as 'g2_connect'. When solve=true (the default) it also solves at the end and reads the result back, returning the same solve summary as g2_solve_canvas: {Solved, Phase, Errors, Warnings, Diagnostics[]}, where each diagnostic is {Id, Name, Nickname, Level (Remark|Warning|Error|Fault), Message}. Solved is true only when the solution completed with no Error or Fault; read the Diagnostics back to see which components failed and why.")]
    public static string Apply(
        RhinoDoc rhDoc,
        [Description("Sliders to place: {Key, Min, Value, Max, Decimals, Name?, X, Y}. Decimals: 0..12.")] SliderSpec[] sliders,
        [Description("Components to place: {Key, Selector, X, Y}. Selector is a Guid (preferred) or component Name.")] ComponentSpec[] components,
        [Description("Wires to create: {SrcKey, Src, DstKey, Dst}. Keys must match a slider or component key above.")] WireSpec[] wires,
        [Description("If true, trigger a new solution at the end.")] bool solve = true)
    {
        if (!GH2_Utils.TryGetDoc(rhDoc, out Document doc))
            return JsonSerializer.Serialize(new ErrResult(false, "Could not get or create GH2 document"));

        var keyToObj = new Dictionary<string, IDocumentObject>(StringComparer.Ordinal);
        var placed = new List<PlacedRef>();
        var placeErrors = new List<PlaceError>();
        var wireResults = new WireResult[wires?.Length ?? 0];

        if (sliders is not null)
        {
            foreach (var s in sliders)
            {
                if (keyToObj.ContainsKey(s.Key))
                {
                    placeErrors.Add(new PlaceError(s.Key, "duplicate key"));
                }
                else if (TryPlaceSlider(doc, s, out var slider, out var err))
                {
                    keyToObj[s.Key] = slider!;
                    placed.Add(new PlacedRef(s.Key, slider!.InstanceId, "Slider"));
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
                if (keyToObj.ContainsKey(c.Key))
                {
                    placeErrors.Add(new PlaceError(c.Key, "duplicate key"));
                }
                else if (TryPlaceComponent(doc, c, out var obj, out var err))
                {
                    keyToObj[c.Key] = obj!;
                    placed.Add(new PlacedRef(c.Key, obj!.InstanceId, GH2_Utils.ClassifyKind(obj.GetType())));
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

        // StartWait (not Start) so the diagnostics we read back reflect the
        // completed solve, giving the authoring loop the same end-to-end signal in
        // one call that g2_solve_canvas returns. The placed/wired work has already
        // happened, so on a solver-infrastructure throw we degrade gracefully:
        // surface the failure as a Fault diagnostic and still return the partial
        // work (key->Guid map, per-step results) rather than throwing out of the tool.
        GH2Diagnostic[] diagnostics = [];
        bool solved = false;
        string phase = "Skipped";
        int errors = 0;
        int warnings = 0;
        if (solve)
        {
            try
            {
                Solution solution = doc.Solution.StartWait();
                List<GH2Diagnostic> collected = GH2_Diagnostics.Collect(doc);
                (errors, warnings) = GH2_Diagnostics.Count(collected);
                diagnostics = collected.ToArray();
                phase = solution.Phase.ToString();
                solved = solution.Phase == SolutionPhase.Completed && errors == 0;
            }
            catch (Exception ex)
            {
                phase = "Faulted";
                errors = 1;
                diagnostics = [new GH2Diagnostic(Guid.Empty, "Solution", "", GH2DiagnosticLevel.Fault, ex.Message)];
            }
        }
        GH2_Utils.Redraw();

        int wiresOk = 0;
        for (int i = 0; i < wireResults.Length; i++) if (wireResults[i].Ok) wiresOk++;

        return JsonSerializer.Serialize(new ApplyResult(
            placed.ToArray(),
            placeErrors.ToArray(),
            wireResults,
            wiresOk,
            solved,
            phase,
            errors,
            warnings,
            diagnostics));
    }

    private static bool TryPlaceSlider(Document doc, SliderSpec s, out NumberSliderObject? slider, out string error)
    {
        slider = null;
        if (s.Decimals < 0 || s.Decimals > 12)
        {
            error = $"Invalid decimals '{s.Decimals}'. Valid range: 0..12.";
            return false;
        }

        var number = new UiNumber(s.Decimals, (decimal)s.Value, (decimal)s.Min, (decimal)s.Max);
        slider = new NumberSliderObject(string.IsNullOrEmpty(s.Name) ? "num" : s.Name!, number);
        doc.Objects.Add(slider, new PointF(s.X, s.Y));
        error = "";
        return true;
    }

    private static bool TryPlaceComponent(Document doc, ComponentSpec c, out IDocumentObject? obj, out string error)
    {
        obj = null;
        if (Guid.TryParse(c.Selector, out Guid guid))
        {
            var proxy = ObjectProxies.FindById(guid);
            if (proxy is null) { error = $"No component with guid '{guid}'"; return false; }
            obj = proxy.Emit();
            if (obj is null) { error = $"Failed to emit '{guid}'"; return false; }
        }
        else
        {
            var matches = new List<ObjectProxy>();
            foreach (var p in ObjectProxies.Proxies)
                if (string.Equals(p.Nomen.Name, c.Selector, StringComparison.OrdinalIgnoreCase))
                    matches.Add(p);

            if (matches.Count == 0) { error = $"No component named '{c.Selector}'"; return false; }
            if (matches.Count > 1)
            {
                var names = string.Join(", ", matches.Select(p => $"{p.Id} ({p.Nomen.Chapter}/{p.Nomen.Section})"));
                error = $"Component name '{c.Selector}' is ambiguous ({matches.Count} matches): {names}. Pass a Guid to disambiguate.";
                return false;
            }
            obj = matches[0].Emit();
            if (obj is null) { error = $"Failed to instantiate '{c.Selector}'"; return false; }
        }
        doc.Objects.Add(obj, new PointF(c.X, c.Y));
        error = "";
        return true;
    }

    private static WireResult WireOne(int idx, WireSpec w, Dictionary<string, IDocumentObject> keyToObj)
    {
        if (!keyToObj.TryGetValue(w.SrcKey, out var srcObj))
            return new WireResult(idx, false, $"src_key '{w.SrcKey}' did not match a placed object");
        if (!keyToObj.TryGetValue(w.DstKey, out var dstObj))
            return new WireResult(idx, false, $"dst_key '{w.DstKey}' did not match a placed object");

        if (!GH2_GraphOps.TryResolveOutput(srcObj, w.Src, out IParameter? srcParam, out string srcErr))
            return new WireResult(idx, false, srcErr);
        if (!GH2_GraphOps.TryResolveInput(dstObj, w.Dst, out IParameter? dstParam, out string dstErr))
            return new WireResult(idx, false, dstErr);

        try
        {
            if (dstParam!.Inputs.IndexOf(srcParam!.InstanceId) < 0)
                Connections.Connect(srcParam!, dstParam!);
        }
        catch (Exception ex)
        {
            return new WireResult(idx, false, ex.Message);
        }

        return new WireResult(idx, true, null);
    }
}
