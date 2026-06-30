using System.Collections.Generic;

namespace RhMcp;

// Serialized transcript shapes. Dumb, immutable, behavior-free: the persisted mirror of the
// live Conversation/Turn/TurnEvent graph, flattened for PersistentSettings + JSON.
internal sealed record TurnEventDto(
    TurnEventKind Kind,
    string Text,
    DateTimeOffset At,
    string Args,
    string Result,
    string Id = "");

internal sealed record TurnDto(
    string Prompt,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<TurnEventDto> Events,
    TokenUsage Usage = default);

internal sealed record ConversationDto(
    string SessionId,
    string AgentName,
    string DocTitle,
    DateTimeOffset StartedAt,
    IReadOnlyList<TurnEventDto> Lifecycle,
    IReadOnlyList<TurnDto> Turns);
