using System.Text.Json.Nodes;

namespace RhMcp.Tools;

// -------------------------------------------------------------------------
// BRIDGE SEAM (added, not upstream). A gateway pair of *static* [McpServerTool]s
// that broker the dynamic Grasshopper-definition tools discovered by GhToolSource.
//
// Why a gateway instead of exposing each definition directly: RhinoMCP's router
// advertises a build-time, code-generated catalog (RouterToolGenerator scans
// /plugin/Tools/ for [McpServerTool] methods). Runtime-discovered tools are
// invisible to it. But because THESE methods are compiled [McpServerTool]s, the
// generator proxies them — so the whole dynamic set becomes reachable through the
// router (and any client), brokered at runtime.
//
// One [McpServerTool] per [McpServerToolType] class: the router generator emits a
// proxy per type and does not support multiple tools in one type (matches the rest
// of /plugin/Tools/, which is one tool per class).
// -------------------------------------------------------------------------

[McpServerToolType]
public static class GhListToolsTool
{
    [McpServerTool("gh_list_tools", "List Grasshopper AI Tools", true, false)]
    [Description(
        "List the Grasshopper-definition tools that can be run via gh_run_tool. Returns "
        + "JSON { tools: [ { name, description, input_schema } ] } where input_schema is the "
        + "JSON Schema for that tool's arguments. Optionally filter with a case-insensitive "
        + "substring `query` matched against name and description; omit to list everything.")]
    public static string GhListTools(
        [Description("Optional case-insensitive substring to filter by name/description. Omit to list all.")]
        string? query = null)
    {
        var src = new GhToolSource(GhToolSource.ResolveDefaultDirectory());
        src.Refresh();

        var arr = new JsonArray();
        foreach (GhTool t in src.All)
        {
            if (!string.IsNullOrWhiteSpace(query)
                && t.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                && (t.Description ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            arr.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = JsonNode.Parse(t.InputSchema.GetRawText()),
            });
        }

        return new JsonObject { ["tools"] = arr }.ToJsonString();
    }
}

[McpServerToolType]
public static class GhRunToolTool
{
    [McpServerTool("gh_run_tool", "Run a Grasshopper AI Tool", false, false)]
    [Description(
        "Run a Grasshopper-definition tool by name. `arguments` is an object whose properties "
        + "match that tool's input_schema (from gh_list_tools). Pass {} for a tool with no "
        + "inputs. Returns JSON { outputs: {...} } plus any solve messages.")]
    public static string GhRunTool(
        [Description("Tool name exactly as returned by gh_list_tools.")]
        string name,
        [Description("Arguments object matching the tool's input_schema. Use {} if it has no inputs.")]
        JsonElement arguments)
    {
        var src = new GhToolSource(GhToolSource.ResolveDefaultDirectory());
        src.Refresh();

        if (!src.TryGet(name, out GhTool tool))
            return new JsonObject
            {
                ["error"] = $"Unknown tool '{name}'. Call gh_list_tools for the current list.",
            }.ToJsonString();

        // RhinoMCP's ToolHandler marshals static [McpServerTool] calls onto the Rhino UI
        // thread, so Execute (which runs NewSolution) is already on the right thread.
        JsonElement args = arguments.ValueKind == JsonValueKind.Object
            ? arguments
            : JsonSerializer.SerializeToElement(new JsonObject());

        return src.Execute(tool, args);
    }
}
