using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Rhino.AI.Models;

namespace Rhino.AI;

internal sealed record ToolTurn : Turn
{

    public string Id { get; }
    public string Name { get; }

    public IReadOnlyList<IToolArg> Args { get; init; }
    public ThoughtSignature? Signature { get; init; }

    public ToolTurn(string id, string name, IReadOnlyList<IToolArg> args, ThoughtSignature? signature = null, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null)
        : base("result")
    {
        Id = id;
        Name = name;
        Args = args;
        Signature = signature;
        
        Role = RoleType.Assistant;
        Success = true;
        Duration = duration;
        TimeStamp = timestamp ?? DateTime.UtcNow;
        TokenCount = tokenCount;

        Data = ToolArgs.Describe(Name, Args);
    }

    public override ITurn Copy() => new ToolTurn(Id, Name, [.. Args], Signature, TimeStamp, Duration, TokenCount);

}
