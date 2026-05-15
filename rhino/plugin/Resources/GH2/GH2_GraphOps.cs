using Grasshopper2.Components;
using Grasshopper2.Doc;
using Grasshopper2.Parameters;

namespace RhMcp.Resources;

public static class GH2_GraphOps
{
    public static bool TryResolveOutput(IDocumentObject obj, string selector, out IParameter? param, out string error)
    {
        param = null;
        error = "";
        if (obj is Component comp)
            return TryPickParam(comp.Parameters.Outputs.ToList(), selector, "output", out param, out error);
        if (obj is IParameter p)
        {
            if (selector is null || selector.Length == 0 || selector == "0") { param = p; return true; }
            error = $"Object '{obj.InstanceId}' is a Param; expected '' or '0' for src, got '{selector}'";
            return false;
        }
        error = $"Object '{obj.InstanceId}' has no outputs";
        return false;
    }

    public static bool TryResolveInput(IDocumentObject obj, string selector, out IParameter? param, out string error)
    {
        param = null;
        error = "";
        if (obj is Component comp)
            return TryPickParam(comp.Parameters.Inputs.ToList(), selector, "input", out param, out error);
        if (obj is IParameter p)
        {
            if (GH2_Utils.IsValueSource(p))
            {
                error = $"destination '{p.DisplayName}' is a value source, not an input";
                return false;
            }
            if (selector is null || selector.Length == 0 || selector == "0") { param = p; return true; }
            error = $"Object '{obj.InstanceId}' is a Param; expected '' or '0' for dst, got '{selector}'";
            return false;
        }
        error = $"Object '{obj.InstanceId}' has no inputs";
        return false;
    }

    private static bool TryPickParam(IList<IParameter> list, string selector, string kind, out IParameter? param, out string error)
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
            if (string.Equals(p.Nomen.Name, selector, StringComparison.OrdinalIgnoreCase)) { param = p; return true; }
        foreach (var p in list)
            if (string.Equals(p.UserName, selector, StringComparison.OrdinalIgnoreCase)) { param = p; return true; }
        foreach (var p in list)
            if (string.Equals(p.DisplayName, selector, StringComparison.OrdinalIgnoreCase)) { param = p; return true; }

        error = $"No {kind} named '{selector}'";
        return false;
    }
}
