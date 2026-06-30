using System.Text.Json.Serialization;

namespace RhMcp;

// Token (and optional cost) accounting for one agent turn, surfaced by the stream-json `result`
// event. Dumb, immutable, behavior-free apart from summing for a session total. CostUsd is a genuine
// absence channel (decimal?): only some CLIs report a dollar cost, so null means "tokens only", not
// "zero". Tokens default to 0 so a terminal event that omits a count still yields a valid record.
// Persisted inside TurnDto, so the two derived readings are [JsonIgnore]'d to keep the stored shape
// to just the three source fields.
internal readonly record struct TokenUsage(int InputTokens, int OutputTokens, decimal? CostUsd = null)
{
    public static TokenUsage Empty { get; } = new(0, 0, null);

    [JsonIgnore]
    public int TotalTokens => InputTokens + OutputTokens;

    [JsonIgnore]
    public bool IsEmpty => InputTokens == 0 && OutputTokens == 0 && CostUsd is null;

    // Session total = sum of per-turn usage. Costs add only when at least one side reports one; the
    // sum stays null while every turn is tokens-only so the UI keeps hiding the cost line.
    public static TokenUsage operator +(TokenUsage a, TokenUsage b) => new(
        a.InputTokens + b.InputTokens,
        a.OutputTokens + b.OutputTokens,
        (a.CostUsd, b.CostUsd) switch
        {
            (null, null) => null,
            (decimal x, null) => x,
            (null, decimal y) => y,
            (decimal x, decimal y) => x + y,
        });
}
