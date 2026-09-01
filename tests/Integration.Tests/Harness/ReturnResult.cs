using System.Text.Json;
using System.Text.Json.Serialization;

namespace RhinoAI.Integration.Tests.Harness;

// Test-side mirror of the router's ReturnResult envelope. Deliberately a copy,
// not a reference — Integration.Tests stays decoupled from the router's
// assembly, and the structural-match-at-deserialization is itself a contract
// test: if the router changes a field name without updating this copy, tests
// fail loud.
//
// `Payload` is JsonElement? (vs the router's JsonNode?) because tests only
// read; JsonElement is the natural shape for inspection and plugs into the
// Ngentic Json.* constraints without conversion.
public sealed record ReturnResult(
    [property: JsonPropertyName("payload")] JsonElement? Payload,
    [property: JsonPropertyName("error")] ErrorInfo? Error = null,
    [property: JsonPropertyName("autoSpawnedSlot")] SlotInfo? AutoSpawnedSlot = null);

public sealed record ErrorInfo(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("crashReportPath")] string? CrashReportPath = null);

public sealed record SlotInfo(
    [property: JsonPropertyName("slotId")] string SlotId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("reason")] string Reason);
