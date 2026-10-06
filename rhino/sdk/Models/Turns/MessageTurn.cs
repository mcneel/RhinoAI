using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Rhino.AI;

public sealed record MessageTurn : ITurn
{

    [JsonPropertyName("role")]
    public RoleType Role { get; }

    [JsonIgnore]
    public bool Success { get; init; } = true;

    [JsonIgnore]
    public int? TokenCount { get; }

    [JsonIgnore]
    public DateTime Timestamp { get; }

    [JsonIgnore]
    public TimeSpan? Duration { get; }

    // TODO : Improve!
    public string Data => string.Concat(Content.OfType<TextContent>().Select(p => p.Text));

    [Obsolete("REMOVE THIS")]
    public string Message => Data;

    private List<IMessageContent> PrivateContent { get; }
    public IReadOnlyList<IMessageContent> Content => PrivateContent;

    public MessageTurn(IEnumerable<IMessageContent> message, RoleType role = RoleType.User, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null)
    {
        PrivateContent = new(message);
        Role = role;
        Timestamp = timestamp ?? DateTime.UtcNow;
        Duration = duration;
        TokenCount = tokenCount;
    }

    public MessageTurn(string message, RoleType role = RoleType.User, DateTime? timestamp = null, TimeSpan? duration = null, int? tokenCount = null)
        : this([new TextContent(message)], role, timestamp, duration, tokenCount)
    {

    }

    public ITurn Copy() => new MessageTurn(Content.Select(c => c.Copy()), Role, Timestamp, Duration, TokenCount) { Success = Success };

}

public interface IMessageContent
{
    public IMessageContent Copy();
}

public sealed record TextContent(string Text) : IMessageContent
{
    public IMessageContent Copy() => this;
}

public sealed record ImageContent(byte[] Bytes, string MediaType) : IMessageContent
{
    public IMessageContent Copy() => new ImageContent([.. Bytes], MediaType);
}

public sealed record FileContent(Uri Uri, string MediaType) : IMessageContent
{
    public IMessageContent Copy() => this;
}

