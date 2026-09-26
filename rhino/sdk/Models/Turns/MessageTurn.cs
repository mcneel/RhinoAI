using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

public sealed record MessageTurn(string Message, RoleType role = RoleType.User, TimeSpan span = default, int? tokenCount = null) : ITurn
{

    [JsonPropertyName("role")]
    public RoleType Role { get; } = role;

    [JsonIgnore]
    public bool Success { get; init; } = true;

    [JsonIgnore]
    public int? TokenCount { get; } = tokenCount;

    [JsonIgnore]
    public TimeSpan Duration { get; } = span;

    public string Data => Message;

    public ITurn Copy() => new MessageTurn(Message, Role, Duration, TokenCount) { Success = Success };

}
