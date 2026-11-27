using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Generic;

using Rhino.AI.Models;

namespace Rhino.AI;

internal static class McpClient
{

    public const string ProtocolVersion = "2025-06-18";

    public delegate Task<JsonNode?> Request(string method, JsonObject? parameters, CancellationToken token);

    public delegate Task Notify(string method, CancellationToken token);

    public sealed record ListedTool(string Name, string Description, bool ReadOnly, bool Destructive, ToolParameter[] Args);

    public static async Task<IReadOnlyList<ListedTool>> ConnectAsync(Request request, Notify notify, CancellationToken token)
    {
        await request("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "Rhino.AI", ["version"] = "0.1.0" },
        }, token).ConfigureAwait(false);

        await notify("notifications/initialized", token).ConfigureAwait(false);

        List<ListedTool> tools = [];
        string? cursor = null;
        do
        {
            JsonObject? page = cursor is null ? null : new JsonObject { ["cursor"] = cursor };
            JsonNode? listed = await request("tools/list", page, token).ConfigureAwait(false);

            foreach (JsonNode? tool in listed?["tools"]?.AsArray() ?? [])
            {
                if (tool?["name"]?.ToString() is not string name)
                    continue;

                JsonNode? annotations = tool["annotations"];
                bool readOnly = annotations?["readOnlyHint"]?.GetValueKind() == JsonValueKind.True;

                // The spec defaults destructiveHint to true, so absence must not read as safe.
                bool destructive = !readOnly && annotations?["destructiveHint"]?.GetValueKind() != JsonValueKind.False;

                tools.Add(new ListedTool(name, tool["description"]?.ToString() ?? "", readOnly, destructive, ToolSchema.ParseParameters(tool["inputSchema"])));
            }

            cursor = listed?["nextCursor"]?.ToString();
        }
        while (cursor is not null);

        return tools;
    }

    public static async Task<ToolReturn> CallToolAsync(Request request, ITool tool, IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        JsonObject arguments = [];
        foreach (IToolArg argument in args)
        {
            if (Declared(tool, argument.Name) is not ToolParameter declared)
                return ToolReturn.UnknownArgument(tool.Name, argument.Name);

            if (ToJson(declared, argument) is not JsonNode value)
                return ToolReturn.Failure($"Argument '{declared.Name}' wants {declared.Type}, got {Describe(argument)}.", $"Call '{tool.Name}' again with '{declared.Name}' as {declared.Type}.");

            arguments[declared.Name] = value;
        }

        foreach (ToolParameter declared in tool.Args)
        {
            if (declared.Required && !arguments.ContainsKey(declared.Name))
                return ToolReturn.MissingArgument(tool.Name, declared.Name);
        }

        JsonNode? result = await request("tools/call", new JsonObject
        {
            ["name"] = tool.Name,
            ["arguments"] = arguments,
        }, token).ConfigureAwait(false);

        StringBuilder text = new();
        foreach (JsonNode? block in result?["content"]?.AsArray() ?? [])
        {
            if (block?["text"]?.ToString() is string value)
                text.AppendLine(value);
        }

        string message = text.ToString().TrimEnd();

        return result?["isError"]?.GetValueKind() == JsonValueKind.True
            ? ToolReturn.Failure(message, $"'{tool.Name}' refused that call. Read the message before calling it again.")
            : ToolReturn.Success(message);
    }

    public static JsonNode? ParseJson(string value)
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
        ToolArgType.Array or ToolArgType.Object => Text(value) is string json ? ParseJson(json) : null,

        _ => throw new ArgumentOutOfRangeException(nameof(arg), arg.Type, "Unknown argument type."),
    };

    private static string? Text(IToolArg arg) => arg switch
    {
        ToolString text => text.Value,
        ToolPath path => path.Value,
        ToolUrl url => url.Value,
        ToolSecret secret => secret.Value,
        ToolJson json => json.Value.GetRawText(),
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

}
