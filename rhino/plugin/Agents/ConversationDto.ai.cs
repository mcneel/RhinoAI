using System.Collections.Generic;

namespace Rhino.AI;

// AI : Would it not make more sense to move all of this to ITurn's?
//      AIInstances.Conversations
internal sealed record TurnEventDto(
    TurnEventKind Kind,
    string Text,
    DateTimeOffset At,
    string Args,
    string Result,
    string Id = "",
    bool Failed = false,
    bool Done = false);

internal sealed record TurnDto(
    string Prompt,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<TurnEventDto> Events,
    TokenUsage Usage = default,
    IReadOnlyList<AttachmentInfo>? Attachments = null,
    string? Error = null);

internal sealed record ConversationDto(
    string SessionId,
    string AgentName,
    string DocTitle,
    DateTimeOffset StartedAt,
    IReadOnlyList<TurnEventDto> Lifecycle,
    IReadOnlyList<TurnDto> Turns,
    bool SessionLost = false);
