using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RhMcp.Router;

// Source-generated JsonSerializerContext for AOT-safe serialization.
// Every type the router serializes via `JsonSerializer.Serialize<T>` or returns
// from a tool method must be listed here. Anonymous types are not allowed — use
// named records below instead. JsonObject/JsonNode are AOT-safe by themselves
// and let us carry dynamic per-tool argument shapes through the JSON-RPC envelope.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
// Router-specific types.
[JsonSerializable(typeof(ChildRhino))]
[JsonSerializable(typeof(IReadOnlyCollection<ChildRhino>))]
[JsonSerializable(typeof(SpawnErrorPayload))]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcRequestParams))]
[JsonSerializable(typeof(SpawnListenerArgs))]
[JsonSerializable(typeof(CloseListenerArgs))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
// Primitives used as tool param/return types. MCP's schema generation walks
// these via our resolver, so they must each be declared explicitly when the
// reflection fallback is disabled (e.g. under AOT or trim).
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(bool?))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(long?))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(float?))]
internal partial class RouterJsonContext : JsonSerializerContext;

// Envelope for the JSON-RPC tools/call payloads ProxyDispatcher and
// RhinoControlClient POST to the plugin's HTTP MCP endpoint.
public sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string Jsonrpc,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonRpcRequestParams Params);

public sealed record JsonRpcRequestParams(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonNode Arguments);

// SpawnSlotTool's catch-block payload. Was an anonymous type pre-AOT.
public sealed record SpawnErrorPayload(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("stackTrace")] string? StackTrace);

// RhinoControlClient args. _router_spawn_listener takes no args; the empty
// record models that while still being a named, source-gen-friendly type.
public sealed record SpawnListenerArgs();

public sealed record CloseListenerArgs(
    [property: JsonPropertyName("port")] int Port);
