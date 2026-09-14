using System;

namespace Rhino.AI;

public sealed record MessageTurn(string Message, TimeSpan span = default, int tokenCount = 1) : ITurn
{

    public bool Success { get; set; } = true;


    public int TokenCount { get; } = 0;

    public TimeSpan Duration { get; }

    public string Data => Message;

    public ITurn Copy() => new MessageTurn(Message, Duration, TokenCount) { Success = Success };

}
