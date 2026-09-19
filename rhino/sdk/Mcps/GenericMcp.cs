using System;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Collections.Generic;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Rhino.AI;

public abstract class GenericMcp : IMcp
{

    public string Name { get; } = "Unnamed MCP";

    private Dictionary<string, ITool> PrivateTools { get; } = [];

    public IReadOnlyDictionary<string, ITool> Tools => PrivateTools;

    public GenericMcp(string name)
    {
        Name = name;
    }

    public void RegisterTool(ITool tool)
    {
        if (!PrivateTools.TryAdd(tool.Name, tool))
            throw new ArgumentException($"MCP '{Name}' already has a tool called '{tool.Name}'.", nameof(tool));

        PrivateTools[tool.Name] = tool;
    }

    public Task<bool> InitAsync(CancellationToken token) => Task.FromResult(true);

    public async Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!PrivateTools.TryGetValue(toolName, out ITool? tool))
            return ToolReturn.Failure($"MCP '{Name}' has no tool '{toolName}'.", "Call one of the tools offered in this session.");

        List<IToolArg> supplied = new(args.Count);
        foreach (IToolArg argument in args)
        {
            if (Declared(tool, argument.Name) is not ToolArg declared)
                return ToolReturn.Failure($"Tool '{toolName}' has no argument '{argument.Name}'.", $"Call '{toolName}' again with only the arguments it declares.");

            supplied.Add(argument);
        }

        foreach (ToolArg declared in tool.Args)
        {
            if (declared.Required && !supplied.Any(s => string.Equals(s.Name, declared.Name)))
                return ToolReturn.Failure($"Tool '{toolName}' requires argument '{declared.Name}'.", $"Call '{toolName}' again with '{declared.Name}' supplied.");
        }

        return await tool.UseAsync(supplied, token).ConfigureAwait(false);
    }

    private static ToolArg? Declared(ITool tool, string name)
    {
        foreach (ToolArg arg in tool.Args)
        {
            if (arg.Name == name)
                return arg;
        }

        return null;
    }

    // Boxing by hand is load bearing: without it every arm converts to JsonNode instead, and a string argument reaches the tool as a JsonValue.
    private static object? ToValue(ToolArg arg, string value) => arg.Type switch
    {
        ToolArgType.String => value,
        ToolArgType.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? (object)number : null,
        ToolArgType.Integer => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer) ? (object)integer : null,
        ToolArgType.Boolean => bool.TryParse(value, out bool flag) ? (object)flag : null,
        ToolArgType.Array or ToolArgType.Object => ParseJson(value),
        _ => throw new ArgumentOutOfRangeException(nameof(arg), arg.Type, "Unknown argument type."),
    };

    private static JsonNode? ParseJson(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Nothing to release: the tools are delegates, not a process or a socket.
    public void Dispose()
    {
    }

}
