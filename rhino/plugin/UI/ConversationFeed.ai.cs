using System.Text;
using System.Text.Json.Nodes;

namespace Rhino.AI.UI;

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
    // Conversation.NoteSessionStarted writes exactly this.
    private const string SessionStartedMarker = "session started";

    private const int TextOverlap = 512;

    private Conversation Source { get; }
    private Action<PanelEvent> Emit { get; }
    private GeneratedImages Produced { get; }

    private List<TurnCursor> Cursors { get; } = new();
    private int LifecycleSent { get; set; }

    // Questions have no identity of their own, so the feed mints one per question and keeps the
    // instance so an answer reaches the object the conversation still holds. Ordered, and plural:
    // one ask_user call can pose several, and a later call appends to the set.
    private List<PosedQuestion> Posed { get; } = new();
    private int QuestionSeq { get; set; }

    private readonly record struct PosedQuestion(PendingQuestion Question, string Id);

    // Same bookkeeping for permission requests: the feed mints the id the card answers with, and
    // withdraws the card when the request is gone.
    private List<PosedPermission> PosedPermissions { get; } = new();
    private int PermissionSeq { get; set; }

    private readonly record struct PosedPermission(PermissionRequest Request, string Id);

    private readonly record struct ToolOutcome(string Result, bool Done, string Title);

    private sealed class TurnCursor
    {
        public string Id = string.Empty;
        public int EventsSent;
        // Consecutive assistant chunks are one block; a tool call or a result closes it.
        public string? OpenTextBlock;
        public int TextBlocks;
        public Dictionary<string, ToolOutcome> ToolOutcomes = new();
        public bool UsageSent;
        public bool Ended;
        public StringBuilder Text = new();
        public int TextScanned;
        public HashSet<string> ImagesSent = new(StringComparer.OrdinalIgnoreCase);
    }

    public ConversationFeed(Conversation source, Action<PanelEvent> emit, string? generatedImagesRoot = null)
    {
        Source = source;
        Emit = emit;
        Produced = new GeneratedImages(generatedImagesRoot);
    }

    // Replay the whole conversation as if it were arriving live. Cheaper than a snapshot type, and
    // it means the attach path, the streaming path and the read-only review of a saved transcript
    // are all the same code.
    public void Replay(bool readOnly = false)
    {
        Cursors.Clear();
        LifecycleSent = 0;
        Posed.Clear();
        PosedPermissions.Clear();

        Emit(new ConversationEvent(new PanelConversation(
            Source.AgentSessionId.ToString(),
            Source.AgentName,
            Source.DocTitle,
            Source.StartedAt.ToString("O"),
            Array.Empty<PanelTurn>(),
            readOnly)));

        Pump();
    }

    public void Pump()
    {
        IReadOnlyList<TurnEvent> lifecycle = Source.Lifecycle;
        for (; LifecycleSent < lifecycle.Count; LifecycleSent++)
        {
            // NoteSessionStarted and NoteSystem share a kind, and the bare "session started" marker
            // tells the user nothing they cannot see from the panel being open. A NoteSystem line is
            // a real fail-soft message (a stale resume target, say) and is still surfaced.
            string text = lifecycle[LifecycleSent].Text;
            if (text == SessionStartedMarker)
                continue;
            Emit(new NoticeEvent("info", text));
        }

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
                    Describe(started.Id, turns[i].Attachments),
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
        PumpPermission();
    }

    private static IReadOnlyList<PanelAttachment> Describe(string turnId, IReadOnlyList<AttachmentInfo> attachments)
    {
        List<PanelAttachment> described = new(attachments.Count);
        for (int i = 0; i < attachments.Count; i++)
            described.Add(PanelAttachment.Describe($"{turnId}-a{i}", attachments[i]));
        return described;
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
            ToolOutcome landed = new(ev.Result, ev.Done, TitleFor(ev));
            if (!cursor.ToolOutcomes.TryGetValue(callId, out ToolOutcome sent) || sent == landed)
                continue;
            cursor.ToolOutcomes[callId] = landed;
            Emit(new TurnToolPatchEvent(cursor.Id, callId, PatchFor(ev)));
            EmitImagesIn(cursor, ev.Result);
        }

        for (; cursor.EventsSent < events.Count; cursor.EventsSent++)
        {
            TurnEvent ev = events[cursor.EventsSent];
            switch (ev.Kind)
            {
                case TurnEventKind.AssistantText:
                    cursor.OpenTextBlock ??= $"{cursor.Id}-b{++cursor.TextBlocks}";
                    Emit(new TurnTextEvent(cursor.Id, cursor.OpenTextBlock, ev.Text));
                    AppendAndScan(cursor, ev.Text);
                    break;

                case TurnEventKind.ToolUse:
                {
                    cursor.OpenTextBlock = null;
                    string callId = CallId(cursor, cursor.EventsSent, ev);
                    cursor.ToolOutcomes[callId] = new ToolOutcome(ev.Result, ev.Done, TitleFor(ev));
                    Emit(new TurnToolEvent(cursor.Id, CallFor(callId, ev)));
                    EmitImagesIn(cursor, ev.Result);
                    break;
                }

                case TurnEventKind.Result:
                    cursor.OpenTextBlock = null;
                    if (!string.IsNullOrWhiteSpace(ev.Text))
                        Emit(new TurnTextEvent(cursor.Id, $"{cursor.Id}-b{++cursor.TextBlocks}", ev.Text));
                    AppendAndScan(cursor, ev.Text);
                    break;
            }
        }

        EmitProducedImages(cursor, turn);

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
            // The turn is over, so a call still waiting on its terminal update never will get one.
            for (int i = 0; i < events.Count; i++)
                if (events[i].Kind == TurnEventKind.ToolUse && !events[i].Done)
                    Emit(new TurnToolPatchEvent(cursor.Id, CallId(cursor, i, events[i]), UnknownPatch));
            Emit(new TurnEndEvent(cursor.Id, "ok", null));
        }
    }

    // A path can straddle two deltas, so the tail already read is read again rather than the whole turn.
    private void AppendAndScan(TurnCursor cursor, string delta)
    {
        if (delta.Length == 0)
            return;

        cursor.Text.Append(delta);
        int from = Math.Max(0, cursor.TextScanned - TextOverlap);
        string window = cursor.Text.ToString(from, cursor.Text.Length - from);
        cursor.TextScanned = cursor.Text.Length;
        EmitImagesIn(cursor, window);
    }

    private void EmitImagesIn(TurnCursor cursor, string text)
    {
        foreach (string path in ImageMentions.In(text))
            EmitImage(cursor, path);
    }

    private void EmitProducedImages(TurnCursor cursor, Turn turn)
    {
        foreach (string path in Produced.Between(turn.StartedAt, turn.CompletedAt ?? DateTimeOffset.UtcNow))
            EmitImage(cursor, path);
    }

    private void EmitImage(TurnCursor cursor, string path)
    {
        if (cursor.ImagesSent.Contains(path) || ServedImages.Publish(path) is not { } image)
            return;

        cursor.ImagesSent.Add(path);
        Emit(new TurnImageEvent(cursor.Id, image));
    }

    // Diff the posed set against the conversation's: drop what is gone, pose what is new. Both
    // directions matter, because questions leave one at a time (a stale clear) as well as together.
    // Same diff as PumpQuestion: withdraw the cards whose request is gone (answered, cancelled, or
    // the turn ended), then post a card for each new one.
    private void PumpPermission()
    {
        Source.TryGetPendingPermissions(out IReadOnlyList<PermissionRequest> pending);

        for (int i = PosedPermissions.Count - 1; i >= 0; i--)
        {
            if (ContainsPermission(pending, PosedPermissions[i].Request))
                continue;
            Emit(new PermissionAskClearEvent(PosedPermissions[i].Id));
            PosedPermissions.RemoveAt(i);
        }

        foreach (PermissionRequest request in pending)
        {
            if (IndexOfPosedPermission(request) >= 0)
                continue;

            string id = $"permission-{++PermissionSeq}";
            PosedPermissions.Add(new PosedPermission(request, id));
            Emit(new PermissionAskEvent(new PanelPermissionAsk(id, request.Tool, request.Title, request.Detail)));
        }
    }

    // The request a card's answer refers to; false when the card is stale, so a click on a request
    // that has already been answered or cancelled does nothing.
    public bool TryResolvePermission(string id, out PermissionRequest request)
    {
        foreach (PosedPermission posed in PosedPermissions)
        {
            if (posed.Id != id)
                continue;
            request = posed.Request;
            return true;
        }
        request = default!;
        return false;
    }

    private static bool ContainsPermission(IReadOnlyList<PermissionRequest> requests, PermissionRequest request)
    {
        foreach (PermissionRequest candidate in requests)
            if (ReferenceEquals(candidate, request))
                return true;
        return false;
    }

    private int IndexOfPosedPermission(PermissionRequest request)
    {
        for (int i = 0; i < PosedPermissions.Count; i++)
            if (ReferenceEquals(PosedPermissions[i].Request, request))
                return i;
        return -1;
    }

    private void PumpQuestion()
    {
        Source.TryGetPendingQuestions(out IReadOnlyList<PendingQuestion> pending);

        for (int i = Posed.Count - 1; i >= 0; i--)
        {
            if (Contains(pending, Posed[i].Question))
                continue;
            Emit(new QuestionClearEvent(Posed[i].Id));
            Posed.RemoveAt(i);
        }

        foreach (PendingQuestion question in pending)
        {
            if (IndexOfPosed(question) >= 0)
                continue;

            string id = $"question-{++QuestionSeq}";
            Posed.Add(new PosedQuestion(question, id));
            Emit(new QuestionEvent(new PanelQuestion(
                id,
                question.Question,
                question.Options,
                question.Mode == AskUserMode.Multi ? "multi" : "single",
                AllowOther: true)));
        }
    }

    // Resolve the question instances a panel answer refers to, so a stale card cannot answer a
    // question that has already been cleared. All-or-nothing: a batch answer is one dispatch, so a
    // single stale id has to fail the whole submit rather than send a partial reply.
    public bool TryResolveQuestions(IReadOnlyList<string> ids, out IReadOnlyList<PendingQuestion> questions)
    {
        List<PendingQuestion> resolved = [];
        foreach (string id in ids)
        {
            int at = IndexOfId(id);
            if (at < 0)
            {
                questions = Array.Empty<PendingQuestion>();
                return false;
            }
            resolved.Add(Posed[at].Question);
        }

        questions = resolved;
        return resolved.Count > 0;
    }

    private int IndexOfId(string id)
    {
        for (int i = 0; i < Posed.Count; i++)
            if (Posed[i].Id == id)
                return i;
        return -1;
    }

    private int IndexOfPosed(PendingQuestion question)
    {
        for (int i = 0; i < Posed.Count; i++)
            if (ReferenceEquals(Posed[i].Question, question))
                return i;
        return -1;
    }

    private static bool Contains(IReadOnlyList<PendingQuestion> questions, PendingQuestion question)
    {
        foreach (PendingQuestion candidate in questions)
            if (ReferenceEquals(candidate, question))
                return true;
        return false;
    }

    // Whether the card the user clicked is still the running call, so a spent chip cannot act.
    public bool IsCallRunning(string callId)
    {
        foreach (TurnCursor cursor in Cursors)
            if (cursor.ToolOutcomes.TryGetValue(callId, out ToolOutcome outcome))
                return !cursor.Ended && !outcome.Done;
        return false;
    }

    // Tool call ids come from the agent, but the id is only guaranteed present on adapters that
    // report one; fall back to the event's position, which is stable because events only append.
    private static string CallId(TurnCursor cursor, int index, TurnEvent ev) =>
        ev.Id.Length > 0 ? ev.Id : $"{cursor.Id}-c{index}";

    private static PanelToolCall CallFor(string callId, TurnEvent ev)
    {
        bool finished = ev.Done;
        bool failed = finished && (ev.Failed || ToolSummary.IsFailure(ev.Result));
        string name = ToolSummary.RemoveUnderscoreUnderscoreNaming(ev.Text);
        return new PanelToolCall(
            callId,
            name,
            TitleFor(ev),
            Payload(ev.Args),
            finished ? failed ? "failed" : "ok" : "running",
            Payload(ev.Result),
            failed ? FailureText(ev.Result) : null,
            ev.At.ToString("O"),
            DurationMs: null,
            Mutated: null,
            ToolChips.For(name, !finished));
    }

    private static PanelToolPatch UnknownPatch { get; } = new("unknown", null, null, null, null, ToolChips.None);

    // While a call is in flight the phrasing is present tense, and one that has Rhino waiting on the
    // user says so instead: a getter on the command line is not something the panel can show, so the
    // card carries it.
    private static string TitleFor(TurnEvent ev) =>
        ToolSummary.Describe(ev.Text, ev.Args, ev.Result, ev.Failed, running: !ev.Done);

    private static PanelToolPatch PatchFor(TurnEvent ev)
    {
        // Not finished: the title changed under us and nothing else did. A null status renames the
        // card and leaves it running, chips and all — closing it here would strand the call.
        if (!ev.Done)
            return new PanelToolPatch(
                null,
                TitleFor(ev),
                null,
                null,
                DurationMs: null,
                ToolChips.For(ToolSummary.RemoveUnderscoreUnderscoreNaming(ev.Text), true));

        bool failed = ev.Failed || ToolSummary.IsFailure(ev.Result);
        return new PanelToolPatch(
            failed ? "failed" : "ok",
            TitleFor(ev),
            Payload(ev.Result),
            failed ? FailureText(ev.Result) : null,
            DurationMs: null,
            ToolChips.None);
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
