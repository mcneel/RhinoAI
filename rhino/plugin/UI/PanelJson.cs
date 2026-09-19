using System.Text.Json.Serialization;

namespace Rhino.AI.UI;

internal static class PanelJson
{
    // WhenWritingNull is load-bearing: the panel reads a present null as a real value, an absent key as unset.
    private static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Serialize(PanelEvent value) => JsonSerializer.Serialize(value, Options);

    public static PanelCommand? Deserialize(string json) =>
        JsonSerializer.Deserialize<PanelCommand>(json, Options);
}
