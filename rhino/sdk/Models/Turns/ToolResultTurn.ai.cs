using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

public sealed record ToolResultTurn(string Id, string ToolName, ToolReturn Return, TimeSpan span = default, int? tokenCount = null) : ITurn
{

    [JsonPropertyName("role")]
    public RoleType Role => RoleType.User;

    [JsonIgnore]
    public bool Success => Return.Result is not ToolResult.Failure;

    [JsonIgnore]
    public int? TokenCount { get; } = tokenCount;

    [JsonIgnore]
    public TimeSpan Duration { get; } = span;

    public string Data => Return.Guidance is string guidance
        ? $"{Return.Message}\n\n{guidance}"
        : Return.Message;

    public ITurn Copy() => new ToolResultTurn(Id, ToolName, Return, Duration, TokenCount);

}
