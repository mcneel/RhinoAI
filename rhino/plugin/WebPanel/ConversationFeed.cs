using System.Text.Json.Nodes;

namespace RhinoAI.WebPanel;

// Translates a Conversation into the panel's incremental event stream.
//
// Conversation only announces "something changed", so this holds the high-water mark of what it has
// already reported and emits the difference. It is deliberately the only place that knows that, so
// when StreamJsonAgent grows real per-block notifications this class is what gets deleted, not
// rewritten in three places.
//
// Pump is not thread-safe and must run on the UI thread; the panel marshals Changed before calling.
internal sealed class ConversationFeed
{
    private Conversation Source { get; }
    private Action<PanelEvent> Emit { get; }

    private List<TurnCursor> Cursors { get; } = new();
    private int LifecycleSent { get; set; }

    // Questions have no identity of their own, so the feed mints one and keeps the instance to map
    // an answer back to the object AskUserPicker arbitrates on.
    private PendingQuestion? PosedQuestion { get; set; }
    private string PosedId { get; set; } = string.Empty;
    private int QuestionSeq { get; set; }

    private sealed class TurnCursor
    {
        public string Id = string.Empty;
        public int EventsSent;
        // Consecutive assistant chunks are one block; a tool call or a result closes it.
        public string? OpenTextBlock;
        public int TextBlocks;
        public Dictionary<string, string> ToolResults = new();
        public bool UsageSent;
        public bool Ended;
    }

    public ConversationFeed(Conversation source, Action<PanelEvent> emit)
    {
        Source = source;
        Emit = emit;
    }

    // Replay the whole conversation as if it were arriving live. Cheaper than a snapshot type, and
    // it means the attach path and the streaming path are the same code.
    public void Replay()
    {
        Cursors.Clear();
        LifecycleSent = 0;
        PosedQuestion = null;
        PosedId = string.Empty;

        Emit(new ConversationEvent(new PanelConversation(
            Source.AgentSessionId.ToString(),
            Source.AgentName,
            Source.DocTitle,
            Source.StartedAt.ToString("O"),
            Array.Empty<PanelTurn>(),
            ReadOnly: false)));

        Pump();
    }

    public void Pump()
    {
        IReadOnlyList<TurnEvent> lifecycle = Source.Lifecycle;
        for (; LifecycleSent < lifecycle.Count; LifecycleSent++)
            Emit(new NoticeEvent("info", lifecycle[LifecycleSent].Text));

        IReadOnlyList<Turn> turns = Source.Turns;
        for (int i = 0; i < turns.Count; i++)
        {
            if (i == Cursors.Count)
            {
                TurnCursor started = new() { Id = $"turn-{i}" };
                Cursors.Add(started);
                Emit(new TurnBeginEvent(new PanelTurn(
                    started.Id,
                    turns[i].Prompt,
                    Array.Empty<object>(),
                    Array.Empty<object>(),
                    turns[i].StartedAt.ToString("O"),
                    Status: "running",
                    Usage: null,
                    Blocks: Array.Empty<object>(),
                    Plan: Array.Empty<object>(),
                    Undoable: true,
                    Error: null)));
            }
            PumpTurn(Cursors[i], turns[i]);
        }

        PumpQuestion();
    }

    private void PumpTurn(TurnCursor cursor, Turn turn)
    {
        IReadOnlyList<TurnEvent> events = turn.Events;

        // A tool result folds into the ToolUse event we already reported, so the earlier part of the
        // list can change under us. Re-check the calls we have sent before appending new events.
        for (int i = 0; i < events.Count && i < cursor.EventsSent; i++)
        {
            TurnEvent ev = events[i];
            if (ev.Kind != TurnEventKind.ToolUse)
                continue;
            string callId = CallId(cursor, i, ev);
            if (!cursor.ToolResults.TryGetValue(callId, out string? sent) || sent == ev.Result)
                continue;
            cursor.ToolResults[callId] = ev.Result;
            Emit(new TurnToolPatchEvent(cursor.Id, callId, PatchFor(ev)));
        }

        for (; cursor.EventsSent < events.Count; cursor.EventsSent++)
        {
            TurnEvent ev = events[cursor.EventsSent];
            switch (ev.Kind)
            {
                case TurnEventKind.AssistantText:
                    cursor.OpenTextBlock ??= $"{cursor.Id}-b{++cursor.TextBlocks}";
                    Emit(new TurnTextEvent(cursor.Id, cursor.OpenTextBlock, ev.Text));
                    break;

                case TurnEventKind.ToolUse:
                {
                    cursor.OpenTextBlock = null;
                    string callId = CallId(cursor, cursor.EventsSent, ev);
                    cursor.ToolResults[callId] = ev.Result;
                    Emit(new TurnToolEvent(cursor.Id, CallFor(callId, ev)));
                    break;
                }

                case TurnEventKind.Result:
                    cursor.OpenTextBlock = null;
                    if (!string.IsNullOrWhiteSpace(ev.Text))
                        Emit(new TurnTextEvent(cursor.Id, $"{cursor.Id}-b{++cursor.TextBlocks}", ev.Text));
                    break;
            }
        }

        if (!cursor.UsageSent && !turn.Usage.IsEmpty)
        {
            cursor.UsageSent = true;
            TokenUsage usage = turn.Usage;
            Emit(new TurnUsageEvent(cursor.Id, new PanelUsage(usage.InputTokens, usage.OutputTokens, usage.CostUsd)));
        }

        // Conversation records completion but not why, so a cancelled turn reports as finished. The
        // panel can render "stopped" the moment Turn learns to carry an outcome.
        if (!cursor.Ended && turn.Completed)
        {
            cursor.Ended = true;
            Emit(new TurnEndEvent(cursor.Id, "ok", null));
        }
    }

    private void PumpQuestion()
    {
        bool pending = Source.TryGetPendingQuestion(out PendingQuestion question);

        if (!pending)
        {
            if (PosedQuestion is null)
                return;
            Emit(new QuestionClearEvent(PosedId));
            PosedQuestion = null;
            PosedId = string.Empty;
            return;
        }

        if (ReferenceEquals(PosedQuestion, question))
            return;

        if (PosedQuestion is not null)
            Emit(new QuestionClearEvent(PosedId));

        PosedQuestion = question;
        PosedId = $"question-{++QuestionSeq}";
        Emit(new QuestionEvent(new PanelQuestion(
            PosedId,
            question.Question,
            question.Options,
            question.Mode == AskUserMode.Multi ? "multi" : "single",
            AllowOther: true)));
    }

    // Resolve the question instance a panel answer refers to, so a stale card cannot answer a
    // question that has already been replaced.
    public bool TryResolveQuestion(string id, out PendingQuestion question)
    {
        if (PosedQuestion is not null && PosedId == id)
        {
            question = PosedQuestion;
            return true;
        }
        question = default!;
        return false;
    }

    // Tool call ids come from the agent, but the id is only guaranteed present on adapters that
    // report one; fall back to the event's position, which is stable because events only append.
    private static string CallId(TurnCursor cursor, int index, TurnEvent ev) =>
        ev.Id.Length > 0 ? ev.Id : $"{cursor.Id}-c{index}";

    private static PanelToolCall CallFor(string callId, TurnEvent ev)
    {
        bool finished = !string.IsNullOrWhiteSpace(ev.Result);
        bool failed = finished && ToolSummary.IsFailure(ev.Result);
        return new PanelToolCall(
            callId,
            ev.Text,
            ToolSummary.Describe(ev.Text, ev.Args, ev.Result),
            Payload(ev.Args),
            finished ? failed ? "failed" : "ok" : "running",
            Payload(ev.Result),
            failed ? FailureText(ev.Result) : null,
            ev.At.ToString("O"),
            DurationMs: null,
            Mutated: null);
    }

    private static PanelToolPatch PatchFor(TurnEvent ev)
    {
        bool failed = ToolSummary.IsFailure(ev.Result);
        return new PanelToolPatch(
            failed ? "failed" : "ok",
            ToolSummary.Describe(ev.Text, ev.Args, ev.Result),
            Payload(ev.Result),
            failed ? FailureText(ev.Result) : null,
            DurationMs: null);
    }

    // Real JSON where the tool produced it, so the panel can pretty-print and highlight it; anything
    // unparseable travels as a string rather than being dropped.
    private static JsonNode? Payload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return JsonValue.Create(raw);
        }
    }

    private static string? FailureText(string resultJson)
    {
        try
        {
            if (JsonNode.Parse(resultJson) is JsonObject root)
                foreach (string name in new[] { "error", "Error", "message", "Message" })
                    if (root[name]?.GetValue<string>() is { Length: > 0 } text)
                        return text;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // A malformed or oddly-shaped payload just means no better message than the generic one.
        }
        return "The tool reported a failure.";
    }
}
