using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// A conversation turn
/// </summary>
[JsonConverter(typeof(TurnJsonConverter))]
public abstract record Turn : ITurn
{

    public string Type { get; init;}

    internal Turn(string type)
    {
        Type = type;
    }

    public RoleType Role { get; init; }
    
    public bool Success { get; init; } = true;

    public int? TokenCount { get; init; }

    public DateTime TimeStamp { get; init; }

    public TimeSpan? Duration { get; init; }

    public virtual string Data { get; init; } = "";

    public abstract ITurn Copy();

}
