using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

public interface ITurn
{

    public RoleType Role { get; }

    [JsonIgnore]
    public bool Success { get; }

    /// <summary>Tokens this turn cost, or null when the endpoint did not break the count down this far.</summary>
    [JsonIgnore]
    public int? TokenCount { get; }

    [JsonIgnore]
    public TimeSpan Duration { get; }

    // [JsonPropertyName]
    public string Data { get; }

    public ITurn Copy();

}

public enum RoleType { System, User, Assistant }
