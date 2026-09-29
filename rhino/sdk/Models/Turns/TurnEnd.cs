using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

public sealed record TurnEnd(StopReason Reason = StopReason.EndTurn, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null) : ITurn
{

    [JsonPropertyName("role")]
    public RoleType Role => RoleType.Assistant;

    [JsonIgnore]
    public bool Success => Reason is not (StopReason.Refusal or StopReason.Error);

    [JsonIgnore]
    public int? TokenCount { get; } = tokenCount;

    [JsonIgnore]
    public DateTime Timestamp { get; } = timestamp ?? DateTime.UtcNow;

    [JsonIgnore]
    public TimeSpan? Duration { get; } = duration;

    public string Data => string.Empty;

    public ITurn Copy() => this;

}

/// <summary>
/// The reason for the turns end
/// </summary>
public enum StopReason { EndTurn, ToolUse, MaxTokens, Refusal, Error, Other }
