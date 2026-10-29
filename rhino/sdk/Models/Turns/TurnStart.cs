using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// A turn start, signalling the beginning of a request
/// </summary>
public sealed record TurnStart : Turn
{

    [JsonConstructor]
    internal TurnStart(DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null)
        : base("start")
    {
        Success = true;
        Role = RoleType.Assistant;

        Role = RoleType.Assistant;
        Duration = duration;
        TimeStamp = timestamp ?? DateTime.UtcNow;
        TokenCount = tokenCount;
    }
    
    public override ITurn Copy() => this;

}
