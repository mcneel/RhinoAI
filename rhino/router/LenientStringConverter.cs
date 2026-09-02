using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RhinoAI.Router;

// Some MCP connector hosts send a string-typed arg as a JSON number/bool, which
// the SDK's default string binding rejects before the tool body runs (surfacing
// as a bare "An error occurred invoking '<tool>'"). Coerce scalars to string so
// binding tolerates the mismatch; the normalised string is what reaches the plugin.
internal sealed class LenientStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            JsonTokenType.Number => RawScalar(ref reader),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => throw new JsonException($"Cannot convert {reader.TokenType} to string."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);

    // Keys are always real JSON property-name strings — the scalar-coercion leniency should only apply to values.
    public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString()!;

    public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WritePropertyName(value);

    // Raw bytes reproduce the number literal exactly — no float round-tripping.
    private static string RawScalar(ref Utf8JsonReader reader)
        => reader.HasValueSequence
            ? Encoding.UTF8.GetString(System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence))
            : Encoding.UTF8.GetString(reader.ValueSpan);
}
