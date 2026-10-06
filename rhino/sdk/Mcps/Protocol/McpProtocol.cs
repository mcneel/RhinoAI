using System;
using System.Threading;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI.Mcps;

internal static class McpProtocol
{

    public static Task<McpResponse> HandleAsync(McpHttpRequest httpRequest, McpSession session, CancellationToken token)
    {
        McpParseResult result = McpRequestParser.Parse(httpRequest);
        if (result is McpParseResult.Rejected rejected)
        {
            return Task.FromResult(rejected.Response);
        }
        else if (result is McpParseResult.Parsed parsed)
        {
            return parsed.Request.ProtocolVersion switch
            {
                "2025-11-25" or "2025-06-18" => HandleLegacyAsync(parsed.Request, session, token),
                "2026-07-28" => HandleModernAsync(parsed.Request, session, token),

                _ => FallbackAsync(parsed.Request, session, token)
            };
        }

        throw new NotImplementedException($"Unknown type {result.GetType()}");
    }

    private static async Task<McpResponse> HandleLegacyAsync(McpRequest request, McpSession session, CancellationToken token)
    {
        if (request.IsNotification)
            return McpResponse.Accepted();

        return request.Method switch
        {
            "initialize" => LegacyInitialize(request, session),
            "ping" => new McpResponse(System.Net.HttpStatusCode.OK, JsonRpc.Result(request.Id, new JsonObject())),
            "tools/list" => ListLegacyTools(request, session),
            "tools/call" => await CallToolAsync(request, session, token),

            _ => MethodNotFound(request)
        };
    }

    private static async Task<McpResponse> HandleModernAsync(McpRequest request, McpSession session, CancellationToken token)
    {
        ;
        return new McpResponse(System.Net.HttpStatusCode.BadRequest, null);
    }

    private static async Task<McpResponse> FallbackAsync(McpRequest request, McpSession session, CancellationToken token)
    {
        ;
        return new McpResponse(System.Net.HttpStatusCode.BadRequest, null);
    }

    private static McpResponse LegacyInitialize(McpRequest request, McpSession session)
    {
        JsonObject result = new()
        {
            ["protocolVersion"] = request.ProtocolVersion,
            ["capabilities"] = new JsonObject()
            {
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject()
            {
                ["name"] = session.Mcp.Name,
                //  typeof(McpProtocol).Assembly.GetName().Version?.ToString() ? 
                ["version"] = "1.0.0" // Doesn't really matter I don't think
            }
        };

        return McpResponse.Json(JsonRpc.Result(request.Id, result));
    }

    private static McpResponse ListLegacyTools(McpRequest request, McpSession session)
    {
        JsonObject result = new()
        {
            ["tools"] = McpTools.List(session),
        };

        return McpResponse.Json(JsonRpc.Result(request.Id, result));
    }

    private static async Task<McpResponse> CallToolAsync(McpRequest request, McpSession session, CancellationToken token)
    {
        // {"name":"get_weather","arguments":{"city":"Miami"}

        string? toolName = request.Params?["name"] is JsonValue nameValue && nameValue.TryGetValue(out string? name) ? name : null;
        if (string.IsNullOrEmpty(toolName))
            return McpResponse.Json(JsonRpc.Error(request.Id, JsonRpcErrorCode.InvalidParams, "tools/call requires a tool name in params.name."));

        List<IToolArg> args = GetArgs(session.Mcp.Tools.GetValueOrDefault(toolName), request.Params?["arguments"]);
        ToolReturn @return = await session.Harness.UseToolAsync(session.Mcp.Name, toolName, args, token);
        return McpResponse.Json(JsonRpc.Result(request.Id, McpTools.ToCallResult(@return)));
    }

    private static List<IToolArg> GetArgs(ITool? tool, JsonNode? jsonNode)
    {
        if (jsonNode is not JsonObject jObj) return [];
        List<IToolArg> args = new(jObj.Count);
        foreach(KeyValuePair<string, JsonNode?> node in jObj)
        {
            IToolArg? arg = ParseArg(tool, node);
            // TODO : Do something about this
            if (arg is null) continue;
            args.Add(arg);
        }

        return args;
    }

    private static IToolArg? ParseArg(ITool? tool, KeyValuePair<string, JsonNode?> node)
        => Models.ToolArgs.FromProperty(tool, node.Key, node.Value);

    private static McpResponse MethodNotFound(McpRequest request)
        => new (System.Net.HttpStatusCode.OK, JsonRpc.Error(request.Id, JsonRpcErrorCode.MethodNotFound,$"Unknown method {request.Method}"));

}
