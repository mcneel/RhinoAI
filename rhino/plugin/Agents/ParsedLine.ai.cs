using System.Collections.Generic;
using Acp;

namespace Rhino.AI;

// A dumb immutable result of parsing one CLI stdout line.
// Updates: the ACP session/update payloads to forward (may be empty).
// IsTurnComplete: true exactly on the CLI's terminal event (Claude 'result', Codex 'turn.completed').
// Reason: the StopReason to resolve the turn with when complete (EndTurn on normal completion).
// Usage: the turn's token/cost accounting, carried ONLY on the terminal event (TokenUsage.Empty
// otherwise). Token usage is a stream-json `result` concept, not an ACP session/update one, so it
// rides the completion shape rather than the Updates list.
// SessionId is set only by a CLI that mints its own id (Codex) rather than accepting the one we pass.
internal record struct ParsedLine(
    IReadOnlyList<SessionUpdate> Updates,
    bool IsTurnComplete,
    StopReason Reason,
    TokenUsage Usage,
    string? SessionId)
{
    public static ParsedLine None { get; } = new([], false, StopReason.EndTurn, TokenUsage.Empty, null);

    public static ParsedLine Emit(params SessionUpdate[] updates) => new(updates, false, StopReason.EndTurn, TokenUsage.Empty, null);

    public static ParsedLine Complete(StopReason reason, TokenUsage usage = default) => new([], true, reason, usage, null);

    // A CLI-reported failure carries its reason into the transcript, so the turn never just stops with nothing shown.
    public static ParsedLine Failed(StopReason reason, params SessionUpdate[] updates) => new(updates, true, reason, TokenUsage.Empty, null);

    public static ParsedLine Session(string sessionId) => new([], false, StopReason.EndTurn, TokenUsage.Empty, sessionId);
}
