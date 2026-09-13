namespace Rhino.AI;

public sealed record MessageTurn(string Message) : ITurn
{

    public bool Success { get; } = true;

    public string Data => Message;

}
