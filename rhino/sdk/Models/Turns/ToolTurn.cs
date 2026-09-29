using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Rhino.AI.Models;

namespace Rhino.AI;

public sealed record ToolTurn(string Id, string Name, IReadOnlyList<IToolArg> Args, ThoughtSignature? Signature = null, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null) : ITurn
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

    public string Data => ToolArgs.Describe(Name, Args);

    public ITurn Copy() => new ToolTurn(Id, Name, [.. Args], Signature, Timestamp, Duration, TokenCount);

}
