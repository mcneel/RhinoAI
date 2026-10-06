using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

public sealed record ThinkingTurn(string Thinking, ThoughtSignature? Signature = null, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null) : ITurn
{

    [JsonPropertyName("role")]
    public RoleType Role => RoleType.Assistant;

    [JsonIgnore]
    public bool Success => true;

    [JsonIgnore]
    public int? TokenCount { get; } = tokenCount;

    [JsonIgnore]
    public DateTime Timestamp { get; } = timestamp ?? DateTime.UtcNow;

    [JsonIgnore]
    public TimeSpan? Duration { get; } = duration;

    public string Data => Thinking;

    public ITurn Copy() => new ThinkingTurn(Thinking, Signature, Timestamp, Duration, TokenCount);

}
