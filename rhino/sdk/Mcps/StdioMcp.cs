using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Rhino.AI;

/// <summary>
/// A standard input output MCP that drives an executable MCP
/// </summary>
public sealed class StdioMcp(string name, Uri process) : IMcp
{

    private const string ProtocolVersion = "2025-06-18";
    private const int RememberedErrorLines = 20;
    private const int GracefulExitMilliseconds = 2000;

    public string Name { get; } = name;

    private Dictionary<string, ITool> PrivateTools { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ITool> Tools => PrivateTools;

    public Uri ProcessPath { get; } = process;

    private Process? Process { get; set; }
    private bool Disposed { get; set; }
    private int LastId { get; set; }

    private StreamReader? Output => Process?.StandardOutput;
    private StreamReader? Error => Process?.StandardError;
    private StreamWriter? Input => Process?.StandardInput;

    private Queue<string> RecentErrors { get; } = new(RememberedErrorLines);
    private Task ErrorDrain { get; set; } = Task.CompletedTask;

    public async Task<bool> InitAsync(CancellationToken token)
    {
        Process = new()
        {
            StartInfo = new(ProcessPath.LocalPath)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        if (!Process.Start()) return false;

        ErrorDrain = DrainErrorAsync();

        await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "Rhino.AI", ["version"] = "0.1.0" },
        }, token).ConfigureAwait(false);

        await NotifyAsync("notifications/initialized", token).ConfigureAwait(false);

        JsonNode? listed = await RequestAsync("tools/list", null, token).ConfigureAwait(false);
        foreach (JsonNode? tool in listed?["tools"]?.AsArray() ?? [])
        {
            if (tool?["name"]?.ToString() is not string name)
                continue;

            JsonNode? annotations = tool["annotations"];
            bool readOnly = annotations?["readOnlyHint"]?.GetValueKind() == JsonValueKind.True;

            // The spec defaults destructiveHint to true, so absence must not read as safe.
            bool destructive = !readOnly && annotations?["destructiveHint"]?.GetValueKind() != JsonValueKind.False;

            PrivateTools[name] = new StdioTool(
                name,
                tool["description"]?.ToString() ?? "",
                readOnly,
                destructive,
                ToArgs(tool["inputSchema"]));
        }

        return true;
    }

    public async Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!PrivateTools.TryGetValue(toolName, out ITool? tool))
            return ToolReturn.Failure($"MCP '{Name}' has no tool '{toolName}'.", "Call one of the tools offered in this session.");

        JsonObject arguments = [];
        foreach (IToolArg argument in args)
        {
            if (Declared(tool, argument.Name) is not ToolParameter declared)
                return ToolReturn.Failure($"Tool '{toolName}' has no argument '{argument.Name}'.", $"Call '{toolName}' again with only the arguments it declares.");

            if (ToJson(declared, argument) is not JsonNode value)
                return ToolReturn.Failure($"Argument '{declared.Name}' wants {declared.Type}, got {Describe(argument)}.", $"Call '{toolName}' again with '{declared.Name}' as {declared.Type}.");

            arguments[declared.Name] = value;
        }

        foreach (ToolParameter declared in tool.Args)
        {
            if (declared.Required && !arguments.ContainsKey(declared.Name))
                return ToolReturn.Failure($"Tool '{toolName}' requires argument '{declared.Name}'.", $"Call '{toolName}' again with '{declared.Name}' supplied.");
        }

        JsonNode? result = await RequestAsync("tools/call", new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = arguments,
        }, token).ConfigureAwait(false);

        StringBuilder text = new();
        foreach (JsonNode? block in result?["content"]?.AsArray() ?? [])
        {
            if (block?["text"]?.ToString() is string value)
                text.AppendLine(value);
        }

        string message = text.ToString().TrimEnd();

        // The spec reports a tool's own failure inside the result, not as a JSON-RPC error.
        return result?["isError"]?.GetValueKind() == JsonValueKind.True
            ? ToolReturn.Failure(message, $"'{toolName}' refused that call. Read the message before calling it again.")
            : ToolReturn.Success(message);
    }

    private static ToolParameter? Declared(ITool tool, string name)
    {
        foreach (ToolParameter arg in tool.Args)
        {
            if (arg.Name == name)
                return arg;
        }

        return null;
    }

    private static JsonNode? ToJson(ToolParameter arg, IToolArg value) => arg.Type switch
    {
        ToolArgType.String or ToolArgType.URL or ToolArgType.FilePath => Text(value) is string text
            ? JsonValue.Create(text)
            : null,
        ToolArgType.Number => value switch
        {
            ToolNumber number => JsonValue.Create(number.Value),
            ToolInt integer => JsonValue.Create((double)integer.Value),
            _ => double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? JsonValue.Create(parsed) : null,
        },
        ToolArgType.Integer => value switch
        {
            ToolInt integer => JsonValue.Create(integer.Value),
            _ => long.TryParse(Text(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? JsonValue.Create(parsed) : null,
        },
        ToolArgType.Boolean => value switch
        {
            ToolBoolean boolean => JsonValue.Create(boolean.Value),
            _ => bool.TryParse(Text(value), out bool parsed) ? JsonValue.Create(parsed) : null,
        },

        // Arrays and objects have no carrier of their own, so they arrive as their JSON text.
        ToolArgType.Array or ToolArgType.Object => Text(value) is string json ? ParseJson(json) : null,

        _ => throw new ArgumentOutOfRangeException(nameof(arg), arg.Type, "Unknown argument type."),
    };

    private static string? Text(IToolArg arg) => arg switch
    {
        ToolString text => text.Value,
        ToolPath path => path.Value,
        ToolUrl url => url.Value,
        ToolSecret secret => secret.Value,
        _ => null,
    };

    private static string Describe(IToolArg arg) => arg switch
    {
        ToolSecret => "a secret",
        ToolBoolean boolean => boolean.Value ? "true" : "false",
        ToolInt integer => integer.Value.ToString(CultureInfo.InvariantCulture),
        ToolNumber number => number.Value.ToString(CultureInfo.InvariantCulture),
        _ => Text(arg) is string text ? $"\"{text}\"" : arg.GetType().Name,
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

    private static ToolParameter[] ToArgs(JsonNode? inputSchema)
    {
        if (inputSchema?["properties"] is not JsonObject properties)
            return [];

        HashSet<string> required = [];
        foreach (JsonNode? name in inputSchema["required"]?.AsArray() ?? [])
        {
            if (name?.ToString() is string value)
                required.Add(value);
        }

        List<ToolParameter> args = new(properties.Count);
        foreach (KeyValuePair<string, JsonNode?> property in properties)
        {
            args.Add(new(
                property.Key,
                property.Value?["description"]?.ToString() ?? "",
                ToArgType(property.Value?["type"]?.ToString()),
                required.Contains(property.Key)));
        }

        return [.. args];
    }

    private static ToolArgType ToArgType(string? type) => type switch
    {
        "number" => ToolArgType.Number,
        "integer" => ToolArgType.Integer,
        "boolean" => ToolArgType.Boolean,
        "array" => ToolArgType.Array,
        "object" => ToolArgType.Object,
        _ => ToolArgType.String,
    };

    private async Task<JsonNode?> RequestAsync(string method, JsonObject? parameters, CancellationToken token)
    {
        JsonObject message = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ++LastId,
            ["method"] = method,
        };
        if (parameters is not null)
            message["params"] = parameters;

        string wanted = LastId.ToString(CultureInfo.InvariantCulture);
        await WriteAsync(message, token).ConfigureAwait(false);

        if (Output is not StreamReader output)
            throw new InvalidOperationException($"MCP '{Name}' is not running.");

        while (await output.ReadLineAsync(token).ConfigureAwait(false) is string line)
        {
            JsonNode? response = JsonNode.Parse(line);
            if (response?["id"]?.ToJsonString() != wanted)
                continue;

            if (response["error"] is JsonNode error)
                throw new InvalidOperationException($"MCP '{Name}' {method} failed: {error.ToJsonString()}{Stderr()}");

            return response["result"];
        }

        throw new InvalidOperationException($"MCP '{Name}' closed stdout during {method}.{Stderr()}");
    }

    private Task NotifyAsync(string method, CancellationToken token)
        => WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }, token);

    private async Task WriteAsync(JsonObject message, CancellationToken token)
    {
        if (Input is not StreamWriter input)
            throw new InvalidOperationException($"MCP '{Name}' is not running.");

        await input.WriteLineAsync(message.ToJsonString().AsMemory(), token).ConfigureAwait(false);
        await input.FlushAsync(token).ConfigureAwait(false);
    }

    private async Task DrainErrorAsync()
    {
        if (Error is not StreamReader error)
            return;

        while (await error.ReadLineAsync().ConfigureAwait(false) is string line)
        {
            lock (RecentErrors)
            {
                RecentErrors.Enqueue(line);
                if (RecentErrors.Count > RememberedErrorLines)
                    RecentErrors.Dequeue();
            }
        }
    }

    private string Stderr()
    {
        lock (RecentErrors)
            return RecentErrors.Count == 0 ? "" : $" stderr: {string.Join(" | ", RecentErrors)}";
    }

    public void Dispose()
    {
        if (Disposed)
            return;

        Disposed = true;

        if (Process is not Process process)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(GracefulExitMilliseconds))
                    process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        process.Dispose();
        Process = null;
        PrivateTools.Clear();
    }

    public bool RegisterTool(ITool tool)
        => PrivateTools.TryAdd(tool.Name, tool);

    private sealed record StdioTool(string Name, string Description, bool ReadOnly, bool Destructive, ToolParameter[] Args) : ITool
    {
        public async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token) => ToolReturn.Refused();
    }

}
