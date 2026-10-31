using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// A turn spent thinking
/// </summary>
public sealed record ThinkingTurn : Turn
{

    public ThoughtSignature? Signature { get; }

    [JsonConstructor]
    private ThinkingTurn(string data, ThoughtSignature? signature, DateTime timeStamp, TimeSpan? duration, int? tokenCount)
        : this(data, signature, (DateTime?)timeStamp, duration, tokenCount)
    {
    }

    internal ThinkingTurn(string Thinking, ThoughtSignature? signature = null, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null)
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
