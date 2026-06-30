using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RhMcp.Router;

// Unified envelope every router tool call returns.
public sealed record ReturnResult(
    [property: JsonPropertyName("payload")] JsonNode? Payload,
    [property: JsonPropertyName("error")] ErrorInfo? Error = null,
    [property: JsonPropertyName("autoSpawnedSlot")] SlotInfo? AutoSpawnedSlot = null)
{
    // Prevents a fun stack overflow
    [JsonIgnore]
    public string AsJson => JsonSerializer.Serialize(this, RouterJsonContext.Default.ReturnResult);
}

// `Code` is the stable kebab-case identifier agents branch on. `Message` ends
// with what the agent should do next. `CrashReportPath` stays typed so agents
// don't have to parse it out of the message.
public sealed record ErrorInfo(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("crashReportPath")] string? CrashReportPath = null);

// Populated only when the call caused a new Rhino to be spawned. `Reason` is
// surfaced to the user verbatim.
public sealed record SlotInfo(
    [property: JsonPropertyName("slotId")] string SlotId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("reason")] string Reason);
