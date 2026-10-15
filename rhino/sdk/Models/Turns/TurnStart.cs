using System;

namespace Rhino.AI;

internal sealed record TurnStart : Turn
{

    public TurnStart(DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null)
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
