using RhMcp.Resources;

using Grasshopper.Kernel;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_ConnectManyTool
{
    public record struct WireSpec(string SrcId, string Src, string DstId, string Dst);
    public record struct Endpoint(Guid Id, string Param);
    public record struct WireResult(int Index, bool Ok, Endpoint? Src, Endpoint? Dst, string? Error);
    public record struct BatchResult(int Count, int OkCount, WireResult[] Wires);

    [McpServerTool(Name = "connect_many")]
    [Description("Wire multiple output→input connections in one call. Same selector semantics as 'connect' (numeric index or Name/NickName; '' or '0' for pure params). A failed wire does not stop later ones; per-wire results are returned. solve runs once at the end.")]
    public static string ConnectMany(
        RhinoDoc _,
        [Description("Array of {SrcId, Src, DstId, Dst} wire descriptors.")] WireSpec[] wires,
        [Description("If true, trigger a new solution after wiring. Set false to batch further.")] bool solve = true)
    {
        if (wires is null || wires.Length == 0) return JsonSerializer.Serialize(new BatchResult(0, 0, Array.Empty<WireResult>()));

        if (!GH1_Utils.TryGetDoc(out GH_Document doc))
            return "No active GH document";

        var results = new WireResult[wires.Length];

        RhinoApp.InvokeAndWait(() =>
        {
            for (int i = 0; i < wires.Length; i++)
                results[i] = WireOne(doc, i, wires[i]);

            if (solve) doc.NewSolution(false);
            GH1_Utils.Redraw();
        });

        int okCount = 0;
        for (int i = 0; i < results.Length; i++) if (results[i].Ok) okCount++;

        return JsonSerializer.Serialize(new BatchResult(wires.Length, okCount, results));
    }

    private static WireResult WireOne(GH_Document doc, int idx, WireSpec w)
    {
        if (!Guid.TryParse(w.SrcId, out Guid srcGuid))
            return new WireResult(idx, false, null, null, $"Invalid src_id '{w.SrcId}'");
        if (!Guid.TryParse(w.DstId, out Guid dstGuid))
            return new WireResult(idx, false, null, null, $"Invalid dst_id '{w.DstId}'");

        var srcObj = doc.FindObject(srcGuid, true);
        if (srcObj is null) return new WireResult(idx, false, null, null, $"Source '{srcGuid}' not found");
        var dstObj = doc.FindObject(dstGuid, true);
        if (dstObj is null) return new WireResult(idx, false, null, null, $"Destination '{dstGuid}' not found");

        if (!GH1_GraphOps.TryResolveOutput(srcObj, w.Src, out IGH_Param? srcParam, out string srcErr))
            return new WireResult(idx, false, null, null, srcErr);
        if (!GH1_GraphOps.TryResolveInput(dstObj, w.Dst, out IGH_Param? dstParam, out string dstErr))
            return new WireResult(idx, false, null, null, dstErr);

        try
        {
            if (!dstParam!.Sources.Contains(srcParam)) dstParam!.AddSource(srcParam);
        }
        catch (Exception ex)
        {
            return new WireResult(idx, false, null, null, ex.Message);
        }

        return new WireResult(
            idx, true,
            new Endpoint(srcObj.InstanceGuid, srcParam!.Name),
            new Endpoint(dstObj.InstanceGuid, dstParam!.Name),
            null);
    }
}
