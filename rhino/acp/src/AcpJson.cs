using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acp;

/// <summary>
/// The single serializer configuration for all ACP messages. Property names are explicit
/// (<c>[JsonPropertyName]</c> on every generated member), so no naming policy is applied;
/// nulls are omitted on write. All polymorphism is driven by generated <see cref="JsonConverter"/>s
/// attached to the union/enum types, so no reflection-based polymorphism is needed (AOT/trim-safe).
/// </summary>
public static class AcpJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };
}
