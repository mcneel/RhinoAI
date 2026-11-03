using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>The operator instruction a transcript opens with. Every endpoint carries it in a field of its own, outside the message list.</summary>
public sealed record SystemTurn : Turn
{
    
    [JsonConstructor]
    internal SystemTurn(string data) : base("system")
    {
        Data = data;
        Role = RoleType.System;
    }

    [JsonIgnore]
    public string Prompt => Data;

    public override ITurn Copy() => this;

}
