using System;

namespace Rhino.AI;

internal sealed record ThinkingTurn : Turn
{

    public ThoughtSignature? Signature { get; }

    public ThinkingTurn(string Thinking, ThoughtSignature? signature = null, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null)
        : base("think")
    {
        Data = Thinking;
        Role = RoleType.Assistant;
        Duration = duration;
        TimeStamp = timestamp ?? DateTime.UtcNow;
        TokenCount = tokenCount;
        Success = true;
        Signature = signature;
    }

    public override ITurn Copy() => new ThinkingTurn(Data, Signature, TimeStamp, Duration, TokenCount);

}
