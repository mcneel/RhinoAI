using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// The record of tokens used by the full turn
/// </summary>
public sealed record UsageTurn : Turn
{

    // Includes cached tokens, which some vendors report separately.
    public int InputTokens { get; }

    public int OutputTokens { get; }

    [JsonConstructor]
    private UsageTurn(int inputTokens, int outputTokens, DateTime timeStamp, TimeSpan? duration)
        : this(inputTokens, outputTokens, (DateTime?)timeStamp, duration)
    {
    }

    internal UsageTurn(int inputTokens, int outputTokens, DateTime? timestamp = null, TimeSpan? duration = null)
        : base("usage")
    {
        Role = RoleType.Assistant;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TokenCount = inputTokens + outputTokens;
        TimeStamp = timestamp ?? DateTime.UtcNow;
        Duration = duration;
    }

    public override ITurn Copy() => this;

}
