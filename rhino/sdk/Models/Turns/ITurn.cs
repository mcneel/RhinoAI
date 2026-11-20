using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// A conversation turn
/// </summary>
[JsonConverter(typeof(TurnJsonConverter))]
public interface ITurn
{
    
    public string Type { get; }

    /// <summary>
    /// The creator of the turn
    /// </summary>
    public RoleType Role { get; }
    
    /// <summary>
    /// The success status of the <see cref="ITurn"/>
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Tokens this turn cost, or null when the endpoint did not break the count down this far.
    /// </summary>
    public int? TokenCount { get; }

    /// <summary>
    /// When the turn was created, in UTC
    /// </summary>
    public DateTime TimeStamp { get; }

    /// <summary>
    /// The duration of the turn
    /// </summary>
    public TimeSpan? Duration { get; }

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
