using Grasshopper.Kernel;

namespace RhMcp.Resources;

public static class GH1_GraphOps
{
    public static bool TryResolveOutput(IGH_DocumentObject obj, string selector, out IGH_Param? param, out string error)
    {
        param = null;
        error = "";
        if (obj is IGH_Component comp)
            return TryPickParam(comp.Params.Output, selector, "output", out param, out error);
        if (obj is IGH_Param p)
        {
            if (selector is null || selector.Length == 0 || selector == "0") { param = p; return true; }
            error = $"Object '{obj.InstanceGuid}' is a Param; expected '' or '0' for src, got '{selector}'";
            return false;
        }
        error = $"Object '{obj.InstanceGuid}' has no outputs";
        return false;
    }

    public static bool TryResolveInput(IGH_DocumentObject obj, string selector, out IGH_Param? param, out string error)
    {
        param = null;
        error = "";
        if (obj is IGH_Component comp)
            return TryPickParam(comp.Params.Input, selector, "input", out param, out error);
        if (obj is IGH_Param p)
        {
            if (GH1_Utils.IsValueSource(p))
            {
                error = $"destination '{p.NickName}' is a value source, not an input";
                return false;
            }
            if (selector is null || selector.Length == 0 || selector == "0") { param = p; return true; }
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
            if (string.Equals(p.Name, selector, StringComparison.OrdinalIgnoreCase)) { param = p; return true; }
        foreach (var p in list)
            if (string.Equals(p.NickName, selector, StringComparison.OrdinalIgnoreCase)) { param = p; return true; }

        error = $"No {kind} named '{selector}'";
        return false;
    }
}
