using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// And end turn, signalling the end of the request
/// </summary>
public sealed record TurnEnd : Turn
{

    public StopReason Reason { get; init; }

    [JsonConstructor]
    private TurnEnd(StopReason reason, string data, DateTime timeStamp, TimeSpan? duration, int? tokenCount)
        : this(reason, data, (DateTime?)timeStamp, duration, tokenCount)
    {
    }

    internal TurnEnd(StopReason reason = StopReason.EndTurn, string? data = null, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null)
        : base("end")
    {
        Role = RoleType.Assistant;
        Reason = reason;
        Success = Reason is not (StopReason.Refusal or StopReason.Error or StopReason.Cancelled);
        Data = data ?? string.Empty;

        Role = RoleType.Assistant;
        Duration = duration;
        TimeStamp = timestamp ?? DateTime.UtcNow;
        TokenCount = tokenCount;
    }

    // TODO : Improve
    public override ITurn Copy() => this;

}

/// <summary>
/// The reason for the turns end
/// </summary>
public enum StopReason { EndTurn, ToolUse, MaxTokens, Refusal, Error, Other, Cancelled }
