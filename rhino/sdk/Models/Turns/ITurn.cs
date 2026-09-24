using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// A conversation turn
/// </summary>
public interface ITurn
{

    /// <summary>
    /// The creator of the turn
    /// </summary>
    public RoleType Role { get; }
    
    /// <summary>
    /// The success status of the <see cref="ITurn"/>
    /// </summary>
    [JsonIgnore]
    public bool Success { get; }

    /// <summary>
    /// Tokens this turn cost, or null when the endpoint did not break the count down this far.
    /// </summary>
    [JsonIgnore]
    public int? TokenCount { get; }
    
    /// <summary>
    /// The turn time
    /// </summary>
    [JsonIgnore]
    public TimeSpan Duration { get; }

    /// <summary>
    /// The turn payload
    /// </summary>
    // [JsonPropertyName]
    public string Data { get; }

    /// <summary>
    /// A copy of the <see cref="ITurn"/>
    /// </summary>
    /// <returns>A copy of the <see cref="ITurn"/></returns>
    public ITurn Copy();

}

/// <summary>
/// The originator of the Turn
/// </summary>
public enum RoleType { System, User, Assistant }
