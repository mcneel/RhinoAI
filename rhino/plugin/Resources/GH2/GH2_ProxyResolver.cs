using Grasshopper2.Framework;
using Grasshopper2.UI;

namespace Rhino.AI.Resources;

internal abstract record GH2_ProxyResolution
{
    public sealed record Found(ObjectProxy Proxy) : GH2_ProxyResolution;
    public sealed record Ambiguous(IReadOnlyList<ObjectProxy> Candidates) : GH2_ProxyResolution;
    public sealed record OnlyDeprecated(IReadOnlyList<ObjectProxy> Candidates) : GH2_ProxyResolution;
    public sealed record NotFound : GH2_ProxyResolution;

    private GH2_ProxyResolution() { }
}

internal record struct GH2_Candidate(Guid Guid, string Name, string Category, string SubCategory, bool IsObsolete, bool IsHidden);

internal static class GH2_ProxyResolver
{
    public static bool IsDeprecated(ObjectProxy proxy) =>
        proxy.Obsolete || proxy.Nomen.Rank == Rank.Hidden;

    public static IReadOnlyList<GH2_Candidate> ToCandidates(IReadOnlyList<ObjectProxy> proxies) =>
        proxies.Select(p => new GH2_Candidate(
            p.Id, p.Nomen.Name, p.Nomen.Chapter, p.Nomen.Section, p.Obsolete, p.Nomen.Rank == Rank.Hidden)).ToArray();

    public const string AmbiguousMessage = "Multiple non-deprecated components share this name; pass a Guid (proxy id) to disambiguate.";
    public const string OnlyDeprecatedMessage = "Only obsolete or hidden components match this name; pass a Guid (proxy id), or set includeDeprecated=true, to use one.";

    public static GH2_ProxyResolution Resolve(string name, bool includeDeprecated)
    {
        List<ObjectProxy> all = [];
        foreach (ObjectProxy proxy in ObjectProxies.Proxies)
        {
            if (string.Equals(proxy.Nomen.Name, name, StringComparison.OrdinalIgnoreCase))
                all.Add(proxy);
        }

        if (all.Count == 0) return new GH2_ProxyResolution.NotFound();

        List<ObjectProxy> live = includeDeprecated ? all : all.Where(p => !IsDeprecated(p)).ToList();
        if (live.Count == 0) return new GH2_ProxyResolution.OnlyDeprecated(all);

        live.Sort((a, b) => ((int)b.Nomen.Rank).CompareTo((int)a.Nomen.Rank));
        if (live.Count > 1 && live[0].Nomen.Rank == live[1].Nomen.Rank)
            return new GH2_ProxyResolution.Ambiguous(live);

        return new GH2_ProxyResolution.Found(live[0]);
    }
}
