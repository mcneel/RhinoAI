using System;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// The result of a tool
/// </summary>
public sealed record ToolResultTurn : Turn
{

    public string Id { get; }
    public string Name { get; }
    public ToolReturn Return { get; }

    [JsonConstructor]
    private ToolResultTurn(string id, string name, ToolReturn @return, DateTime timeStamp, TimeSpan? duration, int? tokenCount)
        : this(id, name, @return, (DateTime?)timeStamp, duration, tokenCount)
    {
    }

    internal ToolResultTurn(string id, string name, ToolReturn @return, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null)
        : base("result")
    {
        Id = id;
        Name = name;
        Role = RoleType.User;
        Return = @return;
        Success = @return.Result is not ToolResult.Failure;
        Duration = duration;
        TimeStamp = timestamp ?? DateTime.UtcNow;
        TokenCount = tokenCount;

        Data = @return.Guidance is string guidance
        ? $"{@return.Message}\n\n{guidance}"
        : @return.Message;
    }

    public override ITurn Copy() => new ToolResultTurn(Id, Name, Return.Copy(), TimeStamp, Duration, TokenCount);

}
