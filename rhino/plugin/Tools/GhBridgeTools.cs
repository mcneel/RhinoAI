using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using RhMcp.Server.GhBridge;

namespace RhMcp.Tools;

// -------------------------------------------------------------------------
// BRIDGE SEAM (added, not upstream). A gateway pair of *static* [McpServerTool]s
// that broker the Grasshopper "AI Tool" definitions published by the separate
// GrasshopperAITools Rhino plug-in, reached over its named pipe.
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
//
// This fork owns no part of the tool library: discovery, ranking, the tools folder
// (resolved by that plug-in from GRASSHOPPER_AITOOLS_DIR) and execution all live on
// the far side of the pipe. Everything here is transport.
// -------------------------------------------------------------------------

[McpServerToolType]
public static class GhListToolsTool
{
    [McpServerTool("gh_list_tools", "List Grasshopper AI Tools", true, false)]
    [BackgroundThread]
    [Description(
        "List the Grasshopper-definition tools that can be run via gh_run_tool. Returns "
        + "JSON { tools: [ { name, description, input_schema, score? } ] } where input_schema is "
        + "the JSON Schema for that tool's arguments. Pass `query` as a natural-language "
        + "description of what you are trying to do: the library is ranked semantically against "
        + "it and only the best matches come back, each with a relevance `score` (higher is "
        + "better). It is a meaning-based search, not a substring match, so 'stack floors into a "
        + "tower' can rank a tool whose name shares no words with the query. Omit `query` to list "
        + "everything, unranked and unscored.")]
    public static async Task<string> GhListTools(
        [Description("Optional natural-language description of the task. Results are ranked "
            + "semantically against it. Omit to list every tool.")]
        string? query = null,
        [Description("Maximum number of ranked results to return when `query` is supplied. "
            + "Defaults to 10 and is capped by the server.")]
        int? topK = null)
    {
        JsonObject parameters = new();
        if (!string.IsNullOrWhiteSpace(query))
            parameters["query"] = query;
        if (topK is int k)
            parameters["top_k"] = k;

        try
        {
            JsonElement result = await GhBridgeCall
                .InvokeAsync(
                    GhBridgeProtocol.Methods.ListTools,
                    parameters.Count == 0 ? null : parameters)
                .ConfigureAwait(false);

            // The bridge's list_tools result already IS { tools: [ ... ] }; pass it through
            // verbatim so a new field on the far side (score was the last one) reaches the
            // caller without a change here.
            return result.ValueKind == JsonValueKind.Undefined
                ? new JsonObject { ["tools"] = new JsonArray() }.ToJsonString()
                : result.GetRawText();
        }
        catch (GhBridgeException ex)
        {
            // The bridge answered, with an error of its own - no install hint warranted.
            return new JsonObject { ["error"] = ex.Message }.ToJsonString();
        }
        catch (Exception ex) when (GhBridgeCall.IsUnreachable(ex))
        {
            return GhBridgeCall.UnreachableJson(ex);
        }
    }
}

[McpServerToolType]
public static class GhRunToolTool
{
    [McpServerTool("gh_run_tool", "Run a Grasshopper AI Tool", false, false)]
    [BackgroundThread]
    [Description(
        "Run a Grasshopper-definition tool by name. `arguments` is an object whose properties "
        + "match that tool's input_schema (from gh_list_tools). Pass {} for a tool with no "
        + "inputs. Returns JSON { outputs: {...} } plus any solve messages. The first call in a "
        + "Rhino session may take up to two minutes while Grasshopper loads; later calls are fast.")]
    public static async Task<string> GhRunTool(
        [Description("Tool name exactly as returned by gh_list_tools.")]
        string name,
        [Description("Arguments object matching the tool's input_schema. Use {} if it has no inputs.")]
        JsonElement arguments)
    {
        JsonObject parameters = new()
        {
            ["tool_name"] = name,
            ["arguments"] = arguments.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(arguments.GetRawText())
                : new JsonObject(),
        };

        try
        {
            JsonElement result = await GhBridgeCall
                .InvokeAsync(GhBridgeProtocol.Methods.CallTool, parameters)
                .ConfigureAwait(false);

            // call_tool answers { content, is_error }; `content` is the engine's own result
            // JSON as a string. Hand that back untouched — it is what the caller wants and
            // re-encoding it would only add an escaping layer.
            if (result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("content", out JsonElement content))
            {
                return content.ValueKind == JsonValueKind.String
                    ? content.GetString() ?? string.Empty
                    : content.GetRawText();
            }

            return result.ValueKind == JsonValueKind.Undefined
                ? new JsonObject { ["error"] = $"The Grasshopper bridge returned no result for '{name}'." }
                    .ToJsonString()
                : result.GetRawText();
        }
        catch (GhBridgeException ex)
        {
            // The bridge answered, with an error of its own - no install hint warranted.
            return new JsonObject { ["error"] = ex.Message }.ToJsonString();
        }
        catch (Exception ex) when (GhBridgeCall.IsUnreachable(ex))
        {
            return GhBridgeCall.UnreachableJson(ex);
        }
    }
}

/// <summary>
/// Shared plumbing for the two gateway tools: one connection per call, and the
/// "bridge is not there" answer.
/// </summary>
internal static class GhBridgeCall
{
    /// <summary>
    /// Connects, issues one request, disconnects. A fresh connection per call is
    /// deliberate — the bridge serves one client at a time, so holding one open between
    /// tool calls would lock every other client out for the life of the Rhino session.
    /// </summary>
    internal static async Task<JsonElement> InvokeAsync(string method, JsonObject? parameters)
    {
        int timeoutMs = GhBridgeProtocol.ResolveTimeoutMs();
        int connectMs = Math.Min(GhBridgeProtocol.ConnectTimeoutMs, timeoutMs);

        using GhBridgePipeClient client = await GhBridgePipeClient
            .ConnectAsync(connectMs)
            .ConfigureAwait(false);

        return await client.InvokeAsync(method, parameters, timeoutMs).ConfigureAwait(false);
    }

    /// <summary>
    /// True when the failure means the bridge could not be reached or did not answer, as
    /// opposed to the bridge answering with an error of its own.
    /// </summary>
    internal static bool IsUnreachable(Exception ex) => ex is TimeoutException or IOException;

    /// <summary>
    /// The structured "bridge is not there" answer. Returned rather than thrown on purpose:
    /// the GrasshopperAITools plug-in is optional, and a Rhino without it must not turn
    /// every gh_* call into a tool error — let alone take the router down with it.
    /// </summary>
    internal static string UnreachableJson(Exception ex) =>
        new JsonObject
        {
            ["error"] = ex.Message,
            ["hint"] = GhBridgeProtocol.MissingBridgeHint,
        }.ToJsonString();
}
