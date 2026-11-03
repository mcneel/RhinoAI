using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rhino.AI;

internal sealed class TurnJsonConverter : JsonConverter<ITurn>
{
    public override ITurn? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;
        string? disc = root.TryGetProperty("Type", out JsonElement d) ? d.GetString() : null;
        // TODO : Is this feasible or nah?
        return disc?.ToLowerInvariant() switch
        {
            "message" => root.Deserialize<MessageTurn>(options),
            "think" => root.Deserialize<ThinkingTurn>(options),
            "tool" => root.Deserialize<ToolTurn>(options),
            "end" => root.Deserialize<TurnEnd>(options),
            "start" => root.Deserialize<TurnStart>(options),
            "result" => root.Deserialize<ToolResultTurn>(options),
            "system" => root.Deserialize<SystemTurn>(options),
            
            _ => throw new JsonException($"Unknown ITurn type: {disc}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, ITurn value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
