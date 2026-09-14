using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public sealed class MemoryMcp : IMcp
{

    public string Name { get; }

    private Dictionary<string, ITool> PrivateTools { get; } = [];

    public IReadOnlyDictionary<string, ITool> Tools => PrivateTools;

    public MemoryMcp(string name)
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

    // A bad call is the model's mistake to correct, so it comes back as a failed ToolReturn rather than an exception.
    public async Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<KeyValuePair<string, string>> args, CancellationToken token)
    {
        if (!PrivateTools.TryGetValue(toolName, out ITool? tool))
            return ToolReturn.Failure($"MCP '{Name}' has no tool '{toolName}'.", "Call one of the tools offered in this session.");

        Dictionary<string, object> supplied = new(args.Count);
        foreach (KeyValuePair<string, string> argument in args)
        {
            if (Declared(tool, argument.Key) is not ToolArg declared)
                return ToolReturn.Failure($"Tool '{toolName}' has no argument '{argument.Key}'.", $"Call '{toolName}' again with only the arguments it declares.");

            if (ToValue(declared, argument.Value) is not object value)
                return ToolReturn.Failure($"Argument '{declared.Name}' wants {declared.Type}, got \"{argument.Value}\".", $"Call '{toolName}' again with '{declared.Name}' as {declared.Type}.");

            supplied[declared.Name] = value;
        }

        foreach (ToolArg declared in tool.Args)
        {
            if (declared.Required && !supplied.ContainsKey(declared.Name))
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

    private static object? ToValue(ToolArg arg, string value) => arg.Type switch
    {
        ToolArgType.String => value,
        ToolArgType.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : null,
        ToolArgType.Integer => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer) ? integer : null,
        ToolArgType.Boolean => bool.TryParse(value, out bool flag) ? flag : null,
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
