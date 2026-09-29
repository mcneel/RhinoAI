using Rhino.AI.Resources;

using Grasshopper;
using Grasshopper.Kernel;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class GH1_SearchComponentsTool
{
    public record struct ProxyHit(
        Guid Guid,
        string Name,
        string NickName,
        string Category,
        string SubCategory,
        string Kind,
        string Description,
        bool IsObsolete,
        bool IsHidden);

    [McpServerTool("g1_search_components", "Search GH1 Components", true, false)]
    [Description("Search the Grasshopper component library. The query is split into words and every word must appear somewhere in Name, NickName, Description, Category or SubCategory (case-insensitive), so 'XY plane' finds 'World XY'. Exact name matches rank first, then names carrying every word. Optional exact-match category/subcategory filters. Excludes obsolete/hidden unless includeDeprecated. Returns up to 'limit' matches.")]
    public static IToolResult Search(
        RhinoDoc _,
        [Description("Substring to match against component Name, NickName, and Description. Case-insensitive.")] string query,
        [Description("Optional exact-match category filter (e.g. 'Maths', 'Params').")] string? category = null,
        [Description("Optional exact-match subcategory filter (e.g. 'Operators').")] string? subcategory = null,
        [Description("Maximum number of results to return.")] int limit = 20,
        [Description("Include obsolete/hidden components (e.g. legacy scripting). Default false.")] bool includeDeprecated = false)
    {
        if (string.IsNullOrEmpty(query))
            return Failure(ToolError.BadArgument, "query is required");

        Coercions coerced = new();
        limit = coerced.Clamp(nameof(limit), limit, 1, int.MaxValue);

        string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        List<(int Rank, ProxyHit Hit)> hits = [];
        foreach (IGH_ObjectProxy p in Instances.ComponentServer.ObjectProxies)
        {
            IGH_InstanceDescription d = p.Desc;
            if (category is not null && !string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
            if (subcategory is not null && !string.Equals(d.SubCategory, subcategory, StringComparison.OrdinalIgnoreCase)) continue;
            if (!includeDeprecated && GH1_ProxyResolver.IsDeprecated(p)) continue;

            if (!tokens.All(t => Match(d.Name, t) || Match(d.NickName, t) || Match(d.Description, t) || Match(d.Category, t) || Match(d.SubCategory, t)))
                continue;

            string kind = GH1_Utils.ClassifyKind(p.Type);

            hits.Add((
                Rank(d.Name, query, tokens),
                new ProxyHit(p.Guid, d.Name, d.NickName, d.Category, d.SubCategory, kind, d.Description, p.Obsolete, p.Exposure == GH_Exposure.hidden)));
        }

        return Success(
            hits
                .OrderBy(h => h.Rank)
                .ThenBy(h => h.Hit.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(h => h.Hit)
                .ToList(),
            coerced.Guidance);
    }

    // Broader matching buries the obvious answer, so an exact name wins, then a name carrying every token.
    private static int Rank(string? name, string query, string[] tokens)
    {
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (tokens.All(t => Match(name, t))) return 1;
        return 2;
    }

    private static bool Match(string? haystack, string needle) =>
        haystack is not null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
