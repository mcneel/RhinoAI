using System.Text.Json.Serialization;

namespace Rhino.AI;

// Persisted inside TurnDto, so the derived readings are [JsonIgnore]'d to keep the stored shape to the two source fields.
internal record struct TokenUsage(int InputTokens, int OutputTokens)
{
    public static TokenUsage Empty { get; } = new(0, 0);

    [JsonIgnore]
    public int TotalTokens => InputTokens + OutputTokens;

    [JsonIgnore]
    public bool IsEmpty => InputTokens == 0 && OutputTokens == 0;

    public static TokenUsage operator +(TokenUsage a, TokenUsage b)
        => new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens);
}
