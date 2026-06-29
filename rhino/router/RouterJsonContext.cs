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
[JsonSerializable(typeof(RhinoCrashReport))]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcRequestParams))]
[JsonSerializable(typeof(SpawnListenerArgs))]
[JsonSerializable(typeof(CloseListenerArgs))]
[JsonSerializable(typeof(QuitAppArgs))]
[JsonSerializable(typeof(Announcement))]
[JsonSerializable(typeof(Departure))]
[JsonSerializable(typeof(ReturnResult))]
[JsonSerializable(typeof(ErrorInfo))]
[JsonSerializable(typeof(SlotInfo))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]

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

// Compact summary of a macOS .ips crash report. Just the agent-actionable bits
// — not the full 100KB body. `path` points at the report on disk for human
// follow-up.
//
// Two stacks are surfaced because Mac Rhino crashes have two distinct layers:
//   - `topFrames`: the native faulting thread (libsystem, xamarin glue, RhCore).
//     Almost always platform plumbing — useful for confirming "this was a managed
//     abort, not a segfault", but no information about the actual cause.
//   - `managedException` + `managedFrames`: the CLR exception type, message, and
//     managed stack frames, parsed out of the `asiBacktraces` field. THIS is
//     the agent-actionable part — it identifies the bug.
public sealed record RhinoCrashReport(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("captureTime")] string? CaptureTime,
    [property: JsonPropertyName("buildVersion")] string? BuildVersion,
    [property: JsonPropertyName("signal")] string? Signal,
    [property: JsonPropertyName("termination")] string? Termination,
    [property: JsonPropertyName("asi")] string? Asi,
    [property: JsonPropertyName("managedException")] string? ManagedException,
    [property: JsonPropertyName("managedFrames")] string[] ManagedFrames,
    [property: JsonPropertyName("topFrames")] string[] TopFrames);

// RhinoControlClient args. _router_spawn_listener takes no args; the empty
// record models that while still being a named, source-gen-friendly type.
public sealed record SpawnListenerArgs();

public sealed record CloseListenerArgs(
    [property: JsonPropertyName("port")] int Port);

public sealed record QuitAppArgs();

// Drop-file shape written by the plugin into <temp>/rhino-mcp-listeners/.
// `v` is a schema version — bump only if the file shape changes in a
// non-additive way. Unknown future fields are ignored on read.
public sealed record Announcement(
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("version")] string? Version);

// Tombstone the plugin drops when a listener closes cleanly. Lets the router
// prune the slot and tell a user-initiated close apart from a crash. Sibling to
// Announcement in the same drop dir, distinguished by a `.gone` extension.
public sealed record Departure(
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("version")] string? Version);
