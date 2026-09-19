using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI;

/// <summary>A Tool inside of an MCP</summary>
public interface ITool
{

    public string Name { get; }
    public string Description { get; }
    public bool ReadOnly { get; }
    public bool Destructive { get; }
    public ToolArg[] Args { get; }

    public Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token);

}

public enum ToolResult { Success, Mixed, Failure }

public record struct ToolReturn(string Message, ToolResult Result, string? Guidance)
{
    public static ToolReturn Failure(string message, string guidance) => new(message, ToolResult.Failure, guidance);
    public static ToolReturn Success(string message) => new(message, ToolResult.Success, null);
    internal static ToolReturn Refused() => new ("Tool use was refused", ToolResult.Failure, "Ask the user what to do");

}

public record struct ToolArg(string Name, string Description, ToolArgType Type, bool Required);

public enum ToolArgType { Unknown = 0, String, URL, FilePath, Number, Integer, Boolean, Array, Object };

public interface IToolArg { public string Name { get; } };

public record struct IToolBoolean(string Name, bool Value) : IToolArg;
public record struct IToolNumber(string Name, double Value) : IToolArg;
public record struct IToolInt(string Name, int Value) : IToolArg;
public record struct IToolPath(string Name, string Value) : IToolArg;
public record struct IToolUrl(string Name, string Value) : IToolArg;
public record struct IToolString(string Name, string Value) : IToolArg;
public record struct IToolSecret(string Name, string Value) : IToolArg;

/// <summary>
/// An MCP is a package of related tools and resources, usually connecting an AI Agent to another application or service
/// </summary>
public interface IMcp : IDisposable
{

    /// <summary>The name of the MCP</summary>
    public string Name { get; }

    /// <summary>The tools offered by this MCP</summary>
    public IReadOnlyDictionary<string, ITool> Tools { get; }

    public Task<bool> InitAsync(CancellationToken token);

    public void RegisterTool(ITool tool);

    public Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token);

}
