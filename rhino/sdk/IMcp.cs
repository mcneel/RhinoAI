using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public interface ITool
{

    public string Name { get; }
    public string Description { get; }
    public bool ReadOnly { get; }
    public bool Destructive { get; }
    public ToolArg[] Args { get; }

    public Task<ToolReturn> UseAsync(IReadOnlyDictionary<string, object> args, CancellationToken token);

}

public enum ToolResult { Success, Mixed, Failure }

public record struct ToolReturn(string Message, ToolResult Result, string? Guidance)
{
    public static ToolReturn Failure(string message, string guidance) => new (message, ToolResult.Failure, guidance);
    public static ToolReturn Success(string message) => new (message, ToolResult.Success, null);
}

public record struct ToolArg(string Name, string Description, ToolArgType Type, bool Required);

public enum ToolArgType { String, Number, Integer, Boolean, Array, Object };

public interface IMcp : IDisposable
{

    public string Name { get; }

    public IReadOnlyDictionary<string, ITool> Tools { get; }

    public Task<bool> InitAsync(CancellationToken token);

    public void RegisterTool(ITool tool);

    public Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<KeyValuePair<string, string>> args, CancellationToken token);

}
