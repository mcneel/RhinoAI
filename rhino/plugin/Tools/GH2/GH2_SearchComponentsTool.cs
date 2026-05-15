using RhMcp.Resources;

using Grasshopper2.Framework;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_SearchComponentsTool
{
    public record struct ProxyHit(
        Guid Guid,
        string Name,
        string Category,
        string SubCategory,
        string Kind,
        string Description);

    [McpServerTool(Name = "search_components")]
    [Description("Search the GH2 component library by substring. Matches Name and Info (case-insensitive). Optional exact-match chapter/section filters. Returns up to 'limit' matches.")]
    public static string Search(
        RhinoDoc _,
        [Description("Substring to match against component Name and Info. Case-insensitive.")] string query,
        [Description("Optional exact-match chapter filter (e.g. 'Maths', 'Params').")] string? category = null,
        [Description("Optional exact-match section filter (e.g. 'Operators').")] string? subcategory = null,
        [Description("Maximum number of results to return.")] int limit = 20)
    {
        if (string.IsNullOrEmpty(query)) return "query is required";

        var hits = new List<ProxyHit>();
        foreach (var p in ObjectProxies.Proxies)
        {
            var n = p.Nomen;
            if (category is not null && !string.Equals(n.Chapter, category, StringComparison.OrdinalIgnoreCase)) continue;
            if (subcategory is not null && !string.Equals(n.Section, subcategory, StringComparison.OrdinalIgnoreCase)) continue;

            if (!Match(n.Name, query) && !Match(n.Info, query)) continue;

            string kind = GH2_Utils.ClassifyKind(p.Type);

            hits.Add(new ProxyHit(p.Id, n.Name, n.Chapter, n.Section, kind, n.Info));
            if (hits.Count >= limit) break;
        }

        return JsonSerializer.Serialize(hits);
    }

    private static bool Match(string? haystack, string needle) =>
        haystack is not null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
