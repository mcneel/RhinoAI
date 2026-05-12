using RhMcp.Resources;

using Grasshopper.Kernel;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_ConnectTool
{
    public record struct Endpoint(Guid Id, string Param);
    public record struct OkResult(bool Ok, Endpoint Src, Endpoint Dst);
    public record struct ErrResult(bool Ok, string Error);

    [McpServerTool(Name = "connect")]
    [Description("Wire an output parameter to an input parameter on the active GH1 canvas. 'src' and 'dst' may be a numeric index or a Name/NickName. For pure params (e.g. a slider) pass '' or '0'.")]
    public static string Connect(
        RhinoDoc _,
        [Description("Guid of the source IGH_DocumentObject.")] string src_id,
        [Description("Output identifier: numeric index, output Name, or output NickName. Use '' or '0' for pure params.")] string src,
        [Description("Guid of the destination IGH_DocumentObject.")] string dst_id,
        [Description("Input identifier: numeric index, input Name, or input NickName. Use '' or '0' for pure params.")] string dst,
        [Description("If true, trigger a new solution after wiring. Set false to batch multiple operations and solve once at the end.")] bool solve = true)
    {
        if (!GH1_Utils.TryGetDoc(out GH_Document doc))
            return Err("No active GH document");

        if (!Guid.TryParse(src_id, out Guid srcGuid)) return Err($"Invalid src_id guid '{src_id}'");
        if (!Guid.TryParse(dst_id, out Guid dstGuid)) return Err($"Invalid dst_id guid '{dst_id}'");

        var srcObj = doc.FindObject(srcGuid, true);
        if (srcObj is null) return Err($"Source object '{srcGuid}' not found");
        var dstObj = doc.FindObject(dstGuid, true);
        if (dstObj is null) return Err($"Destination object '{dstGuid}' not found");

        if (!TryResolveOutput(srcObj, src, out IGH_Param? srcParam, out string srcErr))
            return Err(srcErr);
        if (!TryResolveInput(dstObj, dst, out IGH_Param? dstParam, out string dstErr))
            return Err(dstErr);

        if (dstParam!.Sources.Contains(srcParam))
        {
            return JsonSerializer.Serialize(new OkResult(
                true,
                new Endpoint(srcObj.InstanceGuid, srcParam!.Name),
                new Endpoint(dstObj.InstanceGuid, dstParam!.Name)));
        }

        try
        {
            RhinoApp.InvokeAndWait(() =>
            {
                dstParam!.AddSource(srcParam);
                if (solve) doc.NewSolution(false);
                GH1_Utils.Redraw();
            });
        }
        catch (Exception ex)
        {
            return Err(ex.Message);
        }

        return JsonSerializer.Serialize(new OkResult(
            true,
            new Endpoint(srcObj.InstanceGuid, srcParam!.Name),
            new Endpoint(dstObj.InstanceGuid, dstParam!.Name)));
    }

    private static bool TryResolveOutput(IGH_DocumentObject obj, string selector, out IGH_Param? param, out string error)
    {
        param = null;
        error = "";
        if (obj is IGH_Component comp)
        {
            return TryPickParam(comp.Params.Output, selector, "output", out param, out error);
        }
        if (obj is IGH_Param p)
        {
            if (selector is null || selector.Length == 0 || selector == "0")
            {
                param = p;
                return true;
            }
            error = $"Object '{obj.InstanceGuid}' is a Param; expected '' or '0' for src, got '{selector}'";
            return false;
        }
        error = $"Object '{obj.InstanceGuid}' has no outputs";
        return false;
    }

    private static bool TryResolveInput(IGH_DocumentObject obj, string selector, out IGH_Param? param, out string error)
    {
        param = null;
        error = "";
        if (obj is IGH_Component comp)
        {
            return TryPickParam(comp.Params.Input, selector, "input", out param, out error);
        }
        if (obj is IGH_Param p)
        {
            if (GH1_Utils.IsValueSource(p))
            {
                error = $"destination '{p.NickName}' is a value source, not an input";
                return false;
            }
            if (selector is null || selector.Length == 0 || selector == "0")
            {
                param = p;
                return true;
            }
            error = $"Object '{obj.InstanceGuid}' is a Param; expected '' or '0' for dst, got '{selector}'";
            return false;
        }
        error = $"Object '{obj.InstanceGuid}' has no inputs";
        return false;
    }

    private static bool TryPickParam(IList<IGH_Param> list, string selector, string kind, out IGH_Param? param, out string error)
    {
        param = null;
        error = "";

        if (selector is null || selector.Length == 0)
        {
            if (list.Count == 0) { error = $"Component has no {kind} params"; return false; }
            param = list[0];
            return true;
        }

        if (int.TryParse(selector, out int idx))
        {
            if (idx < 0 || idx >= list.Count) { error = $"{kind} index {idx} out of range (count {list.Count})"; return false; }
            param = list[idx];
            return true;
        }

        foreach (var p in list)
        {
            if (string.Equals(p.Name, selector, StringComparison.OrdinalIgnoreCase)) { param = p; return true; }
        }
        foreach (var p in list)
        {
            if (string.Equals(p.NickName, selector, StringComparison.OrdinalIgnoreCase)) { param = p; return true; }
        }

        error = $"No {kind} named '{selector}'";
        return false;
    }

    private static string Err(string msg) => JsonSerializer.Serialize(new ErrResult(false, msg));
}
