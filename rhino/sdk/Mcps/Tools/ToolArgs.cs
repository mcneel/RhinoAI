using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// Argument Type
/// </summary>
public enum ToolArgType { Unknown = 0, String, URL, FilePath, Number, Integer, Boolean, Array, Object };

/// <summary>
/// An input Argument for Tool calls
/// </summary>
[JsonConverter(typeof(ToolJsonConverter))]
public interface IToolArg
{
    
    /// <summary>
    /// The name of the <see cref="IToolArg"/>
    /// </summary>
    public string Name { get; }

};

/// <summary>
/// An input Argument for Tool calls
/// </summary>
public abstract record ToolArg : IToolArg
{
    
    public string Name { get; }

    public string Type => this.GetType().Name;

    private ToolArg()
    {
        Name = "Error";
    }

    public ToolArg(string name)
    {
        Name = name;
    }

}

/// <summary>
/// A <see cref="bool"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolBoolean(string Name, bool Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="double"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolNumber(string Name, double Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="int"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolInt(string Name, int Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="string"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolPath(string Name, string Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="string"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolUrl(string Name, string Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="string"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolString(string Name, string Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="string"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolSecret(string Name, string Value) : ToolArg(Name) { }

internal sealed class ToolJsonConverter : JsonConverter<IToolArg>
{
    public override IToolArg? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;
        string? disc = root.TryGetProperty("Type", out JsonElement d) ? d.GetString() : null;
        // TODO : Is this feasible or nah?
        return disc?.ToLowerInvariant() switch
        {
            "toolboolean" => root.Deserialize<ToolBoolean>(options),
            "toolnumber" => root.Deserialize<ToolNumber>(options),
            "toolint" => root.Deserialize<ToolInt>(options),
            "toolpath" => root.Deserialize<ToolPath>(options),
            "toolurl" => root.Deserialize<ToolUrl>(options),
            "toolstring" => root.Deserialize<ToolString>(options),
            "toolsecret" => root.Deserialize<ToolSecret>(options),
            
            _ => throw new JsonException($"Unknown ITurn type: {disc}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, IToolArg value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
