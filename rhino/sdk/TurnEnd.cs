using System;

namespace Rhino.AI;

public sealed record TurnEnd : ITurn
{

    public bool Success { get; } = true;

    public int TokenCount { get; } = 0;

    public TimeSpan Duration { get; } = TimeSpan.Zero;

    public string Data { get; } = "";

    public ITurn Copy() => new TurnEnd();

}
