using System;

namespace Rhino.AI;

public interface ITurn
{

    public bool Success { get; }

    public int TokenCount { get; }

    public TimeSpan Duration { get; }

    public string Data { get; }

    public ITurn Copy();

}
