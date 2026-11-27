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
                ToolJson json => JsonNode.Parse(json.Value.GetRawText()),

                _ => throw new NotSupportedException($"{arg.GetType().Name} has no JSON form."),
            };
        }

        return arguments;
    }

    public static string Describe(string name, IReadOnlyList<IToolArg> args)
        => new JsonObject { ["name"] = name, ["args"] = ToJson(args) }.ToJsonString();

    public static JsonNode? Parse(string vendor, string toolName, string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            throw new JsonException($"{vendor} sent malformed arguments for tool '{toolName}': {json}", e);
        }
    }

    public static IReadOnlyList<IToolArg> FromJson(Harness harness, string toolName, JsonNode? args)
        => FromJson(FindTool(harness, toolName), args);

    public static IReadOnlyList<IToolArg> FromJson(ITool? tool, JsonNode? args)
        => FromJson(tool?.Args ?? [], args);

    // JSON cannot tell a path or a URL from any other string, so the tool's own declaration picks the carrier.
    public static IReadOnlyList<IToolArg> FromJson(IReadOnlyList<ToolParameter> declared, JsonNode? args)
    {
        if (args is not JsonObject arguments)
            return [];

        List<IToolArg> pairs = new(arguments.Count);
        foreach (KeyValuePair<string, JsonNode?> argument in arguments)
        {
            if (FromProperty(declared, argument.Key, argument.Value) is IToolArg arg)
                pairs.Add(arg);
        }

        return pairs;
    }

    public static IToolArg? FromProperty(ITool? tool, string name, JsonNode? value)
        => FromProperty(tool?.Args ?? [], name, value);

    private static IToolArg? FromProperty(IReadOnlyList<ToolParameter> declared, string name, JsonNode? value)
    {
        if (value is null)
            return null;

        return value.GetValueKind() switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Array or JsonValueKind.Object => new ToolJson(name, JsonSerializer.SerializeToElement(value)),
            _ => ToArg(name, DeclaredType(declared, name) ?? InferredType(value), value),
        };
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

    private static ITool? FindTool(Harness harness, string wireName)
    {
        if (!ToolSchema.TryParseWireName(wireName, out string mcpName, out string toolName)) return null;
        if (!harness.Mcps.TryGetValue(mcpName, out Mcp? mcp) || mcp is null) return null;

        return mcp.Tools.GetValueOrDefault(toolName);
    }

    private static ToolArgType? DeclaredType(IReadOnlyList<ToolParameter> declared, string name)
    {
        foreach (ToolParameter arg in declared)
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
