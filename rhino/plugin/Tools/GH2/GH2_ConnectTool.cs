using RhMcp.Resources;

using Grasshopper2.Doc;
using Grasshopper2.Parameters;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_ConnectTool
{
    public record struct Endpoint(Guid Id, string Param);
    public record struct OkResult(bool Ok, Endpoint Src, Endpoint Dst);
    public record struct ErrResult(bool Ok, string Error);

    [McpServerTool(Name = "connect")]
    [Description("Wire an output parameter to an input parameter on the active GH2 canvas. 'src' and 'dst' may be a numeric index or a Name/UserName. For pure params (e.g. a slider) pass '' or '0'.")]
    public static string Connect(
        RhinoDoc _,
        [Description("Guid of the source IDocumentObject.")] string src_id,
        [Description("Output identifier: numeric index, output Name, or UserName. Use '' or '0' for pure params.")] string src,
        [Description("Guid of the destination IDocumentObject.")] string dst_id,
        [Description("Input identifier: numeric index, input Name, or UserName. Use '' or '0' for pure params.")] string dst,
        [Description("If true, trigger a new solution after wiring. Set false to batch multiple operations and solve once at the end.")] bool solve = true)
    {
        if (!GH2_Utils.TryGetDoc(out Document doc))
            return Err("No active GH2 document");

        if (!Guid.TryParse(src_id, out Guid srcGuid)) return Err($"Invalid src_id guid '{src_id}'");
        if (!Guid.TryParse(dst_id, out Guid dstGuid)) return Err($"Invalid dst_id guid '{dst_id}'");

        var srcObj = doc.Objects.Find(srcGuid);
        if (srcObj is null) return Err($"Source object '{srcGuid}' not found");
        var dstObj = doc.Objects.Find(dstGuid);
        if (dstObj is null) return Err($"Destination object '{dstGuid}' not found");

        if (!GH2_GraphOps.TryResolveOutput(srcObj, src, out IParameter? srcParam, out string srcErr))
            return Err(srcErr);
        if (!GH2_GraphOps.TryResolveInput(dstObj, dst, out IParameter? dstParam, out string dstErr))
            return Err(dstErr);

        if (dstParam!.Inputs.IndexOf(srcParam!.InstanceId) >= 0)
        {
            return JsonSerializer.Serialize(new OkResult(
                true,
                new Endpoint(srcObj.InstanceId, srcParam!.Nomen.Name),
                new Endpoint(dstObj.InstanceId, dstParam!.Nomen.Name)));
        }

        try
        {
            RhinoApp.InvokeAndWait(() =>
            {
                Connections.Connect(srcParam!, dstParam!);
                if (solve) doc.Solution.Start();
                GH2_Utils.Redraw();
            });
        }
        catch (Exception ex)
        {
            return Err(ex.Message);
        }

        return JsonSerializer.Serialize(new OkResult(
            true,
            new Endpoint(srcObj.InstanceId, srcParam!.Nomen.Name),
            new Endpoint(dstObj.InstanceId, dstParam!.Nomen.Name)));
    }

    private static string Err(string msg) => JsonSerializer.Serialize(new ErrResult(false, msg));
}
