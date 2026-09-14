using System;

namespace Rhino.AI;

public sealed record ThinkingTurn(string Thinking, TimeSpan span = default, int tokenCount = 1) : ITurn
{

    public bool Success { get; } = true;

    public int TokenCount { get; } = tokenCount;

    public TimeSpan Duration { get; } = span;

    public string Data { get; } = "";

    public ITurn Copy() => new ThinkingTurn(Thinking, Duration, TokenCount);

}
