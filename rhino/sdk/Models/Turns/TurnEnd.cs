using System;

namespace Rhino.AI;

// Each parameter in the deserialization constructor on type 'Rhino.AI.TurnEnd'
// must bind to an object property or field on deserialization.
// Each parameter name and type must match with a property or field on the object.
internal sealed record TurnEnd : Turn
{

    public StopReason Reason { get; init; }

    public TurnEnd(StopReason reason = StopReason.EndTurn, string? error = null, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null)
        : base("end")
    {
        Role = RoleType.Assistant;
        Reason = reason;
        Success = Reason is not (StopReason.Refusal or StopReason.Error or StopReason.Cancelled);
        Data = error ?? string.Empty;

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
