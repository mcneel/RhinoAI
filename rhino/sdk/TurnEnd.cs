namespace Rhino.AI;

public sealed record TurnEnd : ITurn
{

    public bool Success { get; }

    public string Data { get; } = "";

}
