using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RhMcp.Router.Tools.GH1;

[McpServerToolType]
public class GH1_SearchComponentsTool(ProxyDispatcher proxy)
{
    [McpServerTool(Name = "search_components")]
    [Description("Search the Grasshopper component library by substring. Matches Name, NickName, and Description (case-insensitive). Optional exact-match category/subcategory filters. Returns up to 'limit' matches.")]
    public Task<string> SearchAsync(
        [Description("Slot ID returned by spawn_slot")] string slot,
        [Description("Substring to match against component Name, NickName, and Description. Case-insensitive.")] string query,
        [Description("Optional exact-match category filter (e.g. 'Maths', 'Params').")] string? category = null,
        [Description("Optional exact-match subcategory filter (e.g. 'Operators').")] string? subcategory = null,
        [Description("Maximum number of results to return.")] int limit = 20,
        CancellationToken ct = default)
    {
        return proxy.CallToolAsync(slot, "search_components",
            new { query, category, subcategory, limit }, ct);
    }
}
