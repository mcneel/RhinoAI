using System.Text.Json.Serialization;

namespace RhinoAI.WebPanel;

internal static class PanelJson
{
    // WhenWritingNull is load-bearing, not tidiness: the panel distinguishes an absent optional from
    // a present null. `"durationMs": null` would read as a real duration and render "0ms", so an
    // unset optional has to be missing from the payload rather than null in it.
    private static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(PanelEvent value) => JsonSerializer.Serialize(value, Options);

    public static PanelCommand? Deserialize(string json) =>
        JsonSerializer.Deserialize<PanelCommand>(json, Options);
}
