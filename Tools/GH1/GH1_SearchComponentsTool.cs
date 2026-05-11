using RhMcp.Resources;

using Grasshopper;
using Grasshopper.Kernel;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_SearchComponentsTool
{
    public record struct ProxyHit(
        Guid Guid,
        string Name,
        string NickName,
        string Category,
        string SubCategory,
        string Kind,
        string Description);

    [McpServerTool(Name = "search_components")]
    [Description("Search the Grasshopper component library by substring. Matches Name, NickName, and Description (case-insensitive). Optional exact-match category/subcategory filters. Returns up to 'limit' matches.")]
    public static string Search(
        RhinoDoc _,
        [Description("Substring to match against component Name, NickName, and Description. Case-insensitive.")] string query,
        [Description("Optional exact-match category filter (e.g. 'Maths', 'Params').")] string? category = null,
        [Description("Optional exact-match subcategory filter (e.g. 'Operators').")] string? subcategory = null,
        [Description("Maximum number of results to return.")] int limit = 20)
    {
        if (string.IsNullOrEmpty(query)) return "query is required";

        var hits = new List<ProxyHit>();
        foreach (IGH_ObjectProxy p in Instances.ComponentServer.ObjectProxies)
        {
            var d = p.Desc;
            if (category is not null && !string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
            if (subcategory is not null && !string.Equals(d.SubCategory, subcategory, StringComparison.OrdinalIgnoreCase)) continue;

            if (!Match(d.Name, query) && !Match(d.NickName, query) && !Match(d.Description, query)) continue;

            string kind = GH1_Utils.ClassifyKind(p.Type);

            hits.Add(new ProxyHit(p.Guid, d.Name, d.NickName, d.Category, d.SubCategory, kind, d.Description));
            if (hits.Count >= limit) break;
        }

        return JsonSerializer.Serialize(hits);
    }

    private static bool Match(string? haystack, string needle) =>
        haystack is not null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
