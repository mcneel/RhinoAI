using System.Collections.Generic;

namespace Rhino.AI;

public sealed record ToolCall
{

    public ToolTurn Request { get; }

    public ToolResultTurn? Result { get; }

    public string Id => Request.Id;

    public string Name => Request.Name;

    public IReadOnlyList<IToolArg> Args => Request.Args;

    public ToolReturn? Return => Result?.Return;

    public bool Success => Return is { Result: not ToolResult.Failure };

    internal ToolCall(ToolTurn request, ToolResultTurn? result)
    {
        Request = request;
        Result = result;
    }

}
