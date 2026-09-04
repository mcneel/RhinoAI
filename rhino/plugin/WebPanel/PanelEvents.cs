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
[JsonDerivedType(typeof(ContextEvent), "context")]
[JsonDerivedType(typeof(HistoryEvent), "history")]
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
[JsonDerivedType(typeof(ZoomEvent), "zoom")]
[JsonDerivedType(typeof(ReloadEvent), "reload")]
internal abstract record PanelEvent;

internal sealed record HelloEvent(PanelHost Host) : PanelEvent;
internal sealed record ThemeEvent(string Scheme, IReadOnlyDictionary<string, string>? Tokens) : PanelEvent;
internal sealed record AgentsEvent(IReadOnlyList<PanelAgent> Agents, string? Active) : PanelEvent;
internal sealed record ContextEvent(IReadOnlyList<PanelContextItem> Items) : PanelEvent;
internal sealed record HistoryEvent(IReadOnlyList<PanelHistoryEntry> Entries) : PanelEvent;
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

// The host owns the menu, the panel owns the zoom ladder, so this carries an intent rather than a
// level and the ladder is never duplicated on this side.
internal sealed record ZoomEvent(string Action) : PanelEvent;
internal sealed record ReloadEvent : PanelEvent;

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

// What the composer's @ menu offers. Kind drives the icon, so it has to be one the panel knows:
// selection, layer, view, document, block, grasshopper or file.
internal sealed record PanelContextItem(string Id, string Kind, string Label, string? Detail, int? Count);

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
    bool? Mutated,
    IReadOnlyList<PanelToolChip> Chips);

internal sealed record PanelToolChip(string Id, string Label, string? Icon, string? Style);

// Chips travels as an empty array, never null: PanelJson drops nulls, so the panel could never clear it.
internal sealed record PanelToolPatch(
    string? Status,
    string? Title,
    JsonNode? Result,
    string? Error,
    int? DurationMs,
    IReadOnlyList<PanelToolChip> Chips);

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

// A row in the history drawer. Resumable is false when the conversation's agent is no longer
// installed or enabled, so the panel offers review only rather than a Resume that would fault.
internal sealed record PanelHistoryEntry(
    string SessionId,
    string Title,
    string Agent,
    string DocTitle,
    string StartedAt,
    int Turns,
    PanelUsage Usage,
    bool Resumable);

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
