using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acp;

// The three JSON-RPC 2.0 frame shapes ACP uses over stdio. Requests carry an id + method;
// notifications carry a method but no id; responses carry an id + (result xor error). These are
// (de)serialized directly: the read loop discriminates by which fields are present.

/// <summary>A JSON-RPC 2.0 request: a method invocation that expects a matching response.</summary>
public sealed record JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; init; } = "2.0";
    [JsonPropertyName("id")] public RequestId Id { get; init; }
    [JsonPropertyName("method")] public required string Method { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Params { get; init; }
}

/// <summary>A JSON-RPC 2.0 response: result xor error, correlated to a request by id.</summary>
public sealed record JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; init; } = "2.0";
    [JsonPropertyName("id")] public RequestId Id { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; init; }
}

/// <summary>A JSON-RPC 2.0 notification: a method invocation with no id and no response.</summary>
public sealed record JsonRpcNotification
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; init; } = "2.0";
    [JsonPropertyName("method")] public required string Method { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Params { get; init; }
}

/// <summary>A JSON-RPC 2.0 error object.</summary>
public sealed record JsonRpcError
{
    [JsonPropertyName("code")] public required int Code { get; init; }
    [JsonPropertyName("message")] public required string Message { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; init; }
}

/// <summary>Standard JSON-RPC 2.0 error codes.</summary>
public enum JsonRpcErrorCode
{
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,
}
