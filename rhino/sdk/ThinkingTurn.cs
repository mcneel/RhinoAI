namespace Rhino.AI;

public sealed record ThinkingTurn(string Thinking) : ITurn
{

    public bool Success { get; }

    public string Data => Thinking;

}
