using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>The operator instruction a transcript opens with. Every endpoint carries it in a field of its own, outside the message list.</summary>
public sealed record SystemTurn(string Prompt) : ITurn
{

    [JsonPropertyName("role")]
    public RoleType Role => RoleType.System;

    [JsonIgnore]
    public bool Success => true;

    [JsonIgnore]
    public int? TokenCount => null;

    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.Zero;

    public string Data => Prompt;

    public ITurn Copy() => this;

}
