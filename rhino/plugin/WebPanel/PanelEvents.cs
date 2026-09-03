using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RhinoAI.WebPanel;

// The host -> panel half of the wire protocol. One closed union, discriminated on "type", matching
// HostEvent in rhino/panel/src/protocol/events.ts.
//
// These are deltas, not snapshots: `turn.text` names the block it extends and `turn.tool.patch`
// names the call it completes, so the panel never re-reads or re-diffs the conversation. The single
// snapshot event exists only to establish a session when a panel first attaches.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloEvent), "hello")]
[JsonDerivedType(typeof(ThemeEvent), "theme")]
[JsonDerivedType(typeof(AgentsEvent), "agents")]
[JsonDerivedType(typeof(ConversationEvent), "conversation")]
[JsonDerivedType(typeof(TurnBeginEvent), "turn.begin")]
[JsonDerivedType(typeof(TurnTextEvent), "turn.text")]
[JsonDerivedType(typeof(TurnToolEvent), "turn.tool")]
[JsonDerivedType(typeof(TurnToolPatchEvent), "turn.tool.patch")]
[JsonDerivedType(typeof(TurnUsageEvent), "turn.usage")]
[JsonDerivedType(typeof(TurnEndEvent), "turn.end")]
[JsonDerivedType(typeof(QuestionEvent), "question")]
[JsonDerivedType(typeof(QuestionClearEvent), "question.clear")]
[JsonDerivedType(typeof(NoticeEvent), "notice")]
[JsonDerivedType(typeof(StatusEvent), "status")]
internal abstract record PanelEvent;

internal sealed record HelloEvent(PanelHost Host) : PanelEvent;
internal sealed record ThemeEvent(string Scheme) : PanelEvent;
internal sealed record AgentsEvent(IReadOnlyList<PanelAgent> Agents, string? Active) : PanelEvent;
internal sealed record ConversationEvent(PanelConversation Snapshot) : PanelEvent;
internal sealed record TurnBeginEvent(PanelTurn Turn) : PanelEvent;
internal sealed record TurnTextEvent(string TurnId, string BlockId, string Delta) : PanelEvent;
internal sealed record TurnToolEvent(string TurnId, PanelToolCall Call) : PanelEvent;
internal sealed record TurnToolPatchEvent(string TurnId, string CallId, PanelToolPatch Patch) : PanelEvent;
internal sealed record TurnUsageEvent(string TurnId, PanelUsage Usage) : PanelEvent;
internal sealed record TurnEndEvent(string TurnId, string Status, string? Error) : PanelEvent;
internal sealed record QuestionEvent(PanelQuestion Question) : PanelEvent;
internal sealed record QuestionClearEvent(string Id) : PanelEvent;
internal sealed record NoticeEvent(string Level, string Text) : PanelEvent;
internal sealed record StatusEvent(string? Text) : PanelEvent;

internal sealed record PanelHost(
    string Product,
    string Version,
    string Platform,
    string DocTitle,
    PanelCapabilities Capabilities);

internal sealed record PanelCapabilities(
    bool Attachments,
    bool ViewportCapture,
    bool UndoTurn,
    bool Grasshopper);

internal sealed record PanelAgent(
    string Name,
    string Label,
    string Model,
    string ModelLabel,
    string Availability,
    string? Detail,
    bool Builtin);

internal sealed record PanelUsage(int InputTokens, int OutputTokens, decimal? CostUsd);

// Args/Result are raw JSON when the tool produced JSON, so the panel can pretty-print and highlight
// them; a payload that will not parse degrades to a JSON string rather than being dropped.
internal sealed record PanelToolCall(
    string Id,
    string Name,
    string Title,
    JsonNode? Args,
    string Status,
    JsonNode? Result,
    string? Error,
    string StartedAt,
    int? DurationMs,
    bool? Mutated);

internal sealed record PanelToolPatch(
    string? Status,
    string? Title,
    JsonNode? Result,
    string? Error,
    int? DurationMs);

// Blocks arrive as their own events, so a turn always begins empty. Attachments/Context/Plan are
// declared because the panel's contract requires them, and are the obvious seams to fill next.
internal sealed record PanelTurn(
    string Id,
    string Prompt,
    IReadOnlyList<object> Attachments,
    IReadOnlyList<object> Context,
    string StartedAt,
    string Status,
    PanelUsage? Usage,
    IReadOnlyList<object> Blocks,
    IReadOnlyList<object> Plan,
    bool Undoable,
    string? Error);

internal sealed record PanelConversation(
    string SessionId,
    string Agent,
    string DocTitle,
    string StartedAt,
    IReadOnlyList<PanelTurn> Turns,
    bool ReadOnly);

internal sealed record PanelQuestion(
    string Id,
    string Question,
    IReadOnlyList<string> Options,
    string Mode,
    bool AllowOther);
