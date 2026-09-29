using Rhino.AI.Resources;

using Grasshopper2.Framework;
using Grasshopper2.UI;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class GH2_SearchComponentsTool
{
    public record struct ProxyHit(
        Guid Guid,
        string Name,
        string Category,
        string SubCategory,
        string Kind,
        string Description,
        bool IsObsolete,
        bool IsHidden);

    [McpServerTool("g2_search_components", "Search GH2 Components", true, false)]
    [Description("Search the GH2 component library. The query is split into words and every word must appear somewhere in Name, Info, Chapter or Section (case-insensitive), so 'XY plane' finds 'World XY'. Exact name matches rank first, then names carrying every word. Optional exact-match chapter/section filters. Excludes obsolete/hidden unless includeDeprecated. Returns up to 'limit' matches.")]
    public static IToolResult Search(
        RhinoDoc _,
        [Description("Substring to match against component Name and Info. Case-insensitive.")] string query,
        [Description("Optional exact-match chapter filter (e.g. 'Maths', 'Params').")] string? category = null,
        [Description("Optional exact-match section filter (e.g. 'Operators').")] string? subcategory = null,
        [Description("Maximum number of results to return.")] int limit = 20,
        [Description("Include obsolete/hidden components (e.g. legacy scripting). Default false.")] bool includeDeprecated = false)
    {
        if (string.IsNullOrEmpty(query))
            return Failure(ToolError.BadArgument, "query is required");

        Coercions coerced = new();
        limit = coerced.Clamp(nameof(limit), limit, 1, int.MaxValue);

        string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        List<(int Order, ProxyHit Hit)> hits = [];
        foreach (ObjectProxy p in ObjectProxies.Proxies)
        {
            Nomen n = p.Nomen;
            if (category is not null && !string.Equals(n.Chapter, category, StringComparison.OrdinalIgnoreCase)) continue;
            if (subcategory is not null && !string.Equals(n.Section, subcategory, StringComparison.OrdinalIgnoreCase)) continue;
            if (!includeDeprecated && GH2_ProxyResolver.IsDeprecated(p)) continue;

            if (!tokens.All(t => Match(n.Name, t) || Match(n.Info, t) || Match(n.Chapter, t) || Match(n.Section, t)))
                continue;

            string kind = GH2_Utils.ClassifyKind(p.Type);

            hits.Add((
                Order(n.Name, query, tokens),
                new ProxyHit(p.Id, n.Name, n.Chapter, n.Section, kind, n.Info, p.Obsolete, p.Nomen.Rank == Rank.Hidden)));
        }

        return Success(
            hits
                .OrderBy(h => h.Order)
                .ThenBy(h => h.Hit.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(h => h.Hit)
                .ToList(),
            coerced.Guidance);
    }

    // Broader matching buries the obvious answer, so an exact name wins, then a name carrying every token.
    private static int Order(string? name, string query, string[] tokens)
    {
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (tokens.All(t => Match(name, t))) return 1;
        return 2;
    }

    private static bool Match(string? haystack, string needle) =>
        haystack is not null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
