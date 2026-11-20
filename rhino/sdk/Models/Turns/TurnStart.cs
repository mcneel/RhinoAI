using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// A turn start, signalling the beginning of a request
/// </summary>
public sealed record TurnStart : Turn
{

    [JsonConstructor]
    private TurnStart(DateTime timeStamp, TimeSpan? duration, int? tokenCount)
        : this((DateTime?)timeStamp, duration, tokenCount)
    {
    }

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
