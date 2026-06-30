using System.Collections.Generic;
using Acp;

namespace RhMcp;

// A dumb immutable result of parsing one CLI stdout line.
// Updates: the ACP session/update payloads to forward (may be empty).
// IsTurnComplete: true exactly on the CLI's terminal event (Claude 'result', Codex 'task_complete').
// Reason: the StopReason to resolve the turn with when complete (EndTurn on normal completion).
// Usage: the turn's token/cost accounting, carried ONLY on the terminal event (TokenUsage.Empty
// otherwise). Token usage is a stream-json `result` concept, not an ACP session/update one, so it
// rides the completion shape rather than the Updates list.
// Build only via the None/Emit/Complete factories so the valid shapes stay the only states.
internal readonly record struct ParsedLine(
    IReadOnlyList<SessionUpdate> Updates,
    bool IsTurnComplete,
    StopReason Reason,
    TokenUsage Usage)
{
    public static ParsedLine None { get; } = new([], false, StopReason.EndTurn, TokenUsage.Empty);

    public static ParsedLine Emit(params SessionUpdate[] updates) => new(updates, false, StopReason.EndTurn, TokenUsage.Empty);

    public static ParsedLine Complete(StopReason reason, TokenUsage usage = default) => new([], true, reason, usage);
}
