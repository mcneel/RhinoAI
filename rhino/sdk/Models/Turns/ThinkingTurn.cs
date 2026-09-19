using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

public sealed record ThinkingTurn(string Thinking, ThoughtSignature? Signature = null, TimeSpan span = default, int? tokenCount = null) : ITurn
{

    [JsonPropertyName("role")]
    public RoleType Role => RoleType.Assistant;

    [JsonIgnore]
    public bool Success => true;

    [JsonIgnore]
    public int? TokenCount { get; } = tokenCount;

    [JsonIgnore]
    public TimeSpan Duration { get; } = span;

    public string Data => Thinking;

    public ITurn Copy() => new ThinkingTurn(Thinking, Signature, Duration, TokenCount);

}
