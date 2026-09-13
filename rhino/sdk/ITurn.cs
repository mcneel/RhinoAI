namespace Rhino.AI;

public interface ITurn
{

    public bool Success { get; }

    // public int MaxTokens { get; }

    public string Data { get; }

}
