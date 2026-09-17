using System.Collections.Generic;

namespace Rhino.AI;

// Serialized transcript shapes. Dumb, immutable, behavior-free: the persisted mirror of the
// live Conversation/Turn/TurnEvent graph, flattened for PersistentSettings + JSON.
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
    IReadOnlyList<AttachmentInfo>? Attachments = null);

// Profile names the panel that owns the transcript; one saved before it existed reads as Rhino's.
internal sealed record ConversationDto(
    string SessionId,
    string AgentName,
    string DocTitle,
    DateTimeOffset StartedAt,
    IReadOnlyList<TurnEventDto> Lifecycle,
    IReadOnlyList<TurnDto> Turns,
    string Profile = "ai");
