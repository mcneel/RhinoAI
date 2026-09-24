using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Models;

internal static class ToolArgs
{

    public static JsonObject ToJson(IReadOnlyList<IToolArg> args)
    {
        JsonObject arguments = [];
        foreach (IToolArg arg in args)
        {
            arguments[arg.Name] = arg switch
            {
                ToolBoolean boolean => JsonValue.Create(boolean.Value),
                ToolInt integer => JsonValue.Create(integer.Value),
                ToolNumber number => JsonValue.Create(number.Value),
                ToolPath path => JsonValue.Create(path.Value),
                ToolUrl url => JsonValue.Create(url.Value),
                ToolString text => JsonValue.Create(text.Value),
                ToolSecret secret => JsonValue.Create(secret.Value),
                
                _ => throw new NotSupportedException($"{arg.GetType().Name} has no JSON form."),
            };
        }

        return arguments;
    }

    public static string Describe(string name, IReadOnlyList<IToolArg> args)
        => new JsonObject { ["name"] = name, ["args"] = ToJson(args) }.ToJsonString();

    public static IReadOnlyList<IToolArg> FromJson(IHarness harness, string toolName, JsonNode? args)
    {
        if (args is not JsonObject arguments)
            return [];

        // JSON cannot tell a path or a URL from any other string, so the tool's own declaration picks the carrier.
        ITool? tool = FindTool(harness, toolName);

        List<IToolArg> pairs = new(arguments.Count);
        foreach (KeyValuePair<string, JsonNode?> argument in arguments)
        {
            if (argument.Value is not JsonNode value || value.GetValueKind() == JsonValueKind.Null)
                continue;

            pairs.Add(ToArg(argument.Key, DeclaredType(tool, argument.Key) ?? InferredType(value), value));
        }

        return pairs;
    }

    private static IToolArg ToArg(string name, ToolArgType type, JsonNode value)
    {
        string text = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();

        return type switch
        {
            ToolArgType.FilePath => new ToolPath(name, text),
            ToolArgType.URL => new ToolUrl(name, text),
            ToolArgType.Integer when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer) => new ToolInt(name, integer),
            ToolArgType.Number when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) => new ToolNumber(name, number),
            ToolArgType.Boolean when bool.TryParse(text, out bool flag) => new ToolBoolean(name, flag),
            _ => new ToolString(name, text),
        };
    }

    private static ITool? FindTool(IHarness harness, string name)
    {
        foreach (IMcp mcp in harness.Mcps.Values)
        {
            if (mcp.Tools.TryGetValue(name, out ITool? tool))
                return tool;
        }

        return null;
    }

    private static ToolArgType? DeclaredType(ITool? tool, string name)
    {
        foreach (ToolParameter arg in tool?.Args ?? [])
        {
            if (string.Equals(arg.Name, name, StringComparison.OrdinalIgnoreCase) && arg.Type != ToolArgType.Unknown)
                return arg.Type;
        }

        return null;
    }

    private static ToolArgType InferredType(JsonNode value) => value.GetValueKind() switch
    {
        JsonValueKind.True or JsonValueKind.False => ToolArgType.Boolean,
        JsonValueKind.Number => ToolArgType.Number,
        JsonValueKind.String => ToolArgType.String,
        JsonValueKind.Array => ToolArgType.Array,
        JsonValueKind.Object => ToolArgType.Object,
        
        _ => ToolArgType.Unknown,
    };

}
