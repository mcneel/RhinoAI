using Grasshopper;
using Grasshopper.Kernel;

namespace RhinoAI.Resources;

public abstract record GH1_ProxyResolution
{
    public sealed record Found(IGH_ObjectProxy Proxy) : GH1_ProxyResolution;
    public sealed record Ambiguous(IReadOnlyList<IGH_ObjectProxy> Candidates) : GH1_ProxyResolution;
    public sealed record OnlyDeprecated(IReadOnlyList<IGH_ObjectProxy> Candidates) : GH1_ProxyResolution;
    public sealed record NotFound : GH1_ProxyResolution;

    private GH1_ProxyResolution() { }
}

public readonly record struct GH1_Candidate(Guid Guid, string Name, string Category, string SubCategory, bool IsObsolete, bool IsHidden);

public readonly record struct GH1_UnresolvedResult(string Error, string Message, IReadOnlyList<GH1_Candidate> Candidates);

public static class GH1_ProxyResolver
{
    // GH_Exposure.hidden is -1 (all bits set), so a HasFlag/bitmask test matches everything. Compare by value.
    public static bool IsDeprecated(IGH_ObjectProxy proxy) =>
        proxy.Obsolete || proxy.Exposure == GH_Exposure.hidden;

    public static IReadOnlyList<GH1_Candidate> ToCandidates(IReadOnlyList<IGH_ObjectProxy> proxies) =>
        proxies.Select(p => new GH1_Candidate(
            p.Guid, p.Desc.Name, p.Desc.Category, p.Desc.SubCategory, p.Obsolete, p.Exposure == GH_Exposure.hidden)).ToArray();

    public const string AmbiguousMessage = "Multiple non-deprecated components share this name; pass a Guid (proxy id) to disambiguate.";
    public const string OnlyDeprecatedMessage = "Only obsolete or hidden components match this name; pass a Guid (proxy id), or set includeDeprecated=true, to use one.";

    public static GH1_ProxyResolution Resolve(string name, bool includeDeprecated)
    {
        List<IGH_ObjectProxy> all = [];
        foreach (IGH_ObjectProxy proxy in Instances.ComponentServer.ObjectProxies)
        {
            if (string.Equals(proxy.Desc.Name, name, StringComparison.OrdinalIgnoreCase))
                all.Add(proxy);
        }

        if (all.Count == 0) return new GH1_ProxyResolution.NotFound();

        List<IGH_ObjectProxy> live = includeDeprecated ? all : all.Where(p => !IsDeprecated(p)).ToList();
        if (live.Count == 0) return new GH1_ProxyResolution.OnlyDeprecated(all);

        live.Sort(CompareProminence);
        if (live.Count > 1 && CompareProminence(live[0], live[1]) == 0)
            return new GH1_ProxyResolution.Ambiguous(live);

        return new GH1_ProxyResolution.Found(live[0]);
    }

    private static int CompareProminence(IGH_ObjectProxy a, IGH_ObjectProxy b)
    {
        int tierCmp = Tier(a.Exposure).CompareTo(Tier(b.Exposure));
        if (tierCmp != 0) return tierCmp;
        return (IsObscure(a.Exposure) ? 1 : 0).CompareTo(IsObscure(b.Exposure) ? 1 : 0);
    }

    private static int Tier(GH_Exposure exposure) => (int)exposure & ~(int)GH_Exposure.obscure;

    private static bool IsObscure(GH_Exposure exposure) => ((int)exposure & (int)GH_Exposure.obscure) != 0;
}
