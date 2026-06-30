using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acp;

/// <summary>
/// A JSON-RPC request id: a number, a string, or absent. Used to correlate a response with the
/// request that produced it. Numbers and strings are kept distinct so equality/hashing match the
/// wire value exactly.
/// </summary>
[JsonConverter(typeof(RequestIdJsonConverter))]
public readonly struct RequestId : IEquatable<RequestId>
{
    public enum IdKind { None, Number, String }

    public IdKind Kind { get; }
    public long Number { get; }
    public string? Text { get; }

    private RequestId(IdKind kind, long number, string? text)
    {
        Kind = kind;
        Number = number;
        Text = text;
    }

    public bool IsValid => Kind != IdKind.None;

    public static RequestId Of(long number) => new(IdKind.Number, number, null);

    public static RequestId Of(string text) => new(IdKind.String, 0, text);

    public bool Equals(RequestId other) => Kind == other.Kind && Kind switch
    {
        IdKind.Number => Number == other.Number,
        IdKind.String => Text == other.Text,
        _ => true,
    };

    public override bool Equals(object? obj) => obj is RequestId other && Equals(other);

    public override int GetHashCode() => Kind switch
    {
        IdKind.Number => Number.GetHashCode(),
        IdKind.String => Text?.GetHashCode() ?? 0,
        _ => 0,
    };

    public override string ToString() => Kind switch
    {
        IdKind.Number => Number.ToString(),
        IdKind.String => Text ?? string.Empty,
        _ => "(none)",
    };
}

internal sealed class RequestIdJsonConverter : JsonConverter<RequestId>
{
    public override RequestId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Number => RequestId.Of(reader.GetInt64()),
        JsonTokenType.String => RequestId.Of(reader.GetString()!),
        JsonTokenType.Null => default,
        _ => throw new JsonException($"Invalid JSON-RPC id token: {reader.TokenType}"),
    };

    public override void Write(Utf8JsonWriter writer, RequestId value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case RequestId.IdKind.Number:
                writer.WriteNumberValue(value.Number);
                break;
            case RequestId.IdKind.String:
                writer.WriteStringValue(value.Text);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
