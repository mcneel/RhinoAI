using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

public sealed record TurnEnd(StopReason Reason = StopReason.EndTurn, int? tokenCount = null) : ITurn
{

    [JsonPropertyName("role")]
    public RoleType Role => RoleType.Assistant;

    [JsonIgnore]
    public bool Success => Reason is not (StopReason.Refusal or StopReason.Error);

    [JsonIgnore]
    public int? TokenCount { get; } = tokenCount;

    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.Zero;

    public string Data => string.Empty;

    public ITurn Copy() => this;

}

public enum StopReason { EndTurn, ToolUse, MaxTokens, Refusal, Error, Other }
