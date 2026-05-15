using RhMcp.Resources;

using Grasshopper2.Doc;
using Grasshopper2.Parameters;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_ConnectManyTool
{
    public record struct WireSpec(string SrcId, string Src, string DstId, string Dst);
    public record struct Endpoint(Guid Id, string Param);
    public record struct WireResult(int Index, bool Ok, Endpoint? Src, Endpoint? Dst, string? Error);
    public record struct BatchResult(int Count, int OkCount, WireResult[] Wires);

    [McpServerTool(Name = "connect_many")]
    [Description("Wire multiple output→input connections in one call on the active GH2 canvas. Same selector semantics as 'connect'. A failed wire does not stop later ones; per-wire results are returned. solve runs once at the end.")]
    public static string ConnectMany(
        RhinoDoc _,
        [Description("Array of {SrcId, Src, DstId, Dst} wire descriptors.")] WireSpec[] wires,
        [Description("If true, trigger a new solution after wiring. Set false to batch further.")] bool solve = true)
    {
        if (wires is null || wires.Length == 0) return JsonSerializer.Serialize(new BatchResult(0, 0, Array.Empty<WireResult>()));

        if (!GH2_Utils.TryGetDoc(out Document doc))
            return "No active GH2 document";

        var results = new WireResult[wires.Length];

        RhinoApp.InvokeAndWait(() =>
        {
            for (int i = 0; i < wires.Length; i++)
                results[i] = WireOne(doc, i, wires[i]);

            if (solve) doc.Solution.Start();
            GH2_Utils.Redraw();
        });

        int okCount = 0;
        for (int i = 0; i < results.Length; i++) if (results[i].Ok) okCount++;

        return JsonSerializer.Serialize(new BatchResult(wires.Length, okCount, results));
    }

    private static WireResult WireOne(Document doc, int idx, WireSpec w)
    {
        if (!Guid.TryParse(w.SrcId, out Guid srcGuid))
            return new WireResult(idx, false, null, null, $"Invalid src_id '{w.SrcId}'");
        if (!Guid.TryParse(w.DstId, out Guid dstGuid))
            return new WireResult(idx, false, null, null, $"Invalid dst_id '{w.DstId}'");

        var srcObj = doc.Objects.Find(srcGuid);
        if (srcObj is null) return new WireResult(idx, false, null, null, $"Source '{srcGuid}' not found");
        var dstObj = doc.Objects.Find(dstGuid);
        if (dstObj is null) return new WireResult(idx, false, null, null, $"Destination '{dstGuid}' not found");

        if (!GH2_GraphOps.TryResolveOutput(srcObj, w.Src, out IParameter? srcParam, out string srcErr))
            return new WireResult(idx, false, null, null, srcErr);
        if (!GH2_GraphOps.TryResolveInput(dstObj, w.Dst, out IParameter? dstParam, out string dstErr))
            return new WireResult(idx, false, null, null, dstErr);

        try
        {
            if (dstParam!.Inputs.IndexOf(srcParam!.InstanceId) < 0)
                Connections.Connect(srcParam!, dstParam!);
        }
        catch (Exception ex)
        {
            return new WireResult(idx, false, null, null, ex.Message);
        }

        return new WireResult(
            idx, true,
            new Endpoint(srcObj.InstanceId, srcParam!.Nomen.Name),
            new Endpoint(dstObj.InstanceId, dstParam!.Nomen.Name),
            null);
    }
}
