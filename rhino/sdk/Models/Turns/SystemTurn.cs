using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>The operator instruction a transcript opens with. Every endpoint carries it in a field of its own, outside the message list.</summary>
public sealed record SystemTurn : Turn
{
    
    internal SystemTurn(string prompt) : base("system")
    {
        Data = prompt;
        Role = RoleType.System;
    }

    [JsonIgnore]
    public string Prompt => Data;

    public override ITurn Copy() => this;

}
