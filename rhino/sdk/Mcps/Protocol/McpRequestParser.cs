using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rhino.AI.Mcps;

internal static class McpRequestParser
{

    private const string ModernVersionKey = "io.modelcontextprotocol/protocolVersion";

    public static McpParseResult Parse(McpHttpRequest http)
    {
        if (!IsAllowedOrigin(http.Origin))
            return Reject(McpResponse.Empty(HttpStatusCode.Forbidden));

        if (!string.Equals(http.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            return Reject(McpResponse.Empty(HttpStatusCode.MethodNotAllowed));

        if (ParseBody(http.Body) is not JsonObject body)
            return Invalid(null, JsonRpcErrorCode.ParseError, "Body is not a JSON object.");

        JsonNode? id = body["id"]?.DeepClone();

        if (ReadString(body["jsonrpc"]) != "2.0")
            return Invalid(id, JsonRpcErrorCode.InvalidRequest, "Only JSON-RPC 2.0 is supported.");

        if (ReadString(body["method"]) is not string method)
            return Invalid(id, JsonRpcErrorCode.InvalidRequest, "Request has no method.");

        JsonNode? paramsNode = body["params"];
        if (paramsNode is not null and not JsonObject)
            return Invalid(id, JsonRpcErrorCode.InvalidRequest, "Params must be an object.");

        JsonObject? @params = paramsNode as JsonObject;
        if (ReadVersion(method, @params, http.ProtocolVersionHeader) is not string version)
            return Invalid(id, JsonRpcErrorCode.InvalidParams, "Request has no protocol version.");

        return new McpParseResult.Parsed(new McpRequest(id, method, @params, version));
    }

    private static string? ReadVersion(string method, JsonObject? @params, string? protocolVersionHeader)
    {
        string? version = null;
        version ??= ReadString(@params?["_meta"]?[ModernVersionKey]);

        if (string.Equals(method, "initialize", StringComparison.OrdinalIgnoreCase))
        {
            version ??= ReadString(@params?["protocolVersion"]);
        }
        else
        {
            version ??= protocolVersionHeader;
        }
        
        return version ?? throw new NotImplementedException($"Unknown protocol!");
    }

    private static bool IsAllowedOrigin(string? origin)
        => string.IsNullOrEmpty(origin) || (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && uri.IsLoopback);

    private static JsonNode? ParseBody(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static McpParseResult Reject(McpResponse response) => new McpParseResult.Rejected(response);

    private static McpParseResult Invalid(JsonNode? id, JsonRpcErrorCode code, string message)
        => Reject(McpResponse.Json(JsonRpc.Error(id, code, message), HttpStatusCode.BadRequest));

}
