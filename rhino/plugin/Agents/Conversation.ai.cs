using System.Text;

namespace Rhino.AI;

// Stream chunk kinds; SessionStarted lives at conversation level, not inside a turn.
internal enum TurnEventKind
{
    AssistantText,
    ToolUse,
    Result,
    SessionStarted,
}

// Args/Result are empty for kinds that don't carry them; ToolUse holds the tool's input JSON in
// Args and (once the matching tool_result arrives) its output in Result so a chip can expand both.
// Id is the tool call id on a ToolUse event so its later result can be matched back to it; empty
// for every other kind.
// Done is the terminal update arriving, not the tool having produced output: some succeed silently.
internal sealed record TurnEvent(
    TurnEventKind Kind,
    string Text,
    DateTimeOffset At,
    string Args = "",
    string Result = "",
    string Id = "",
    bool Failed = false,
    bool Done = false);

// Mutated only while it is the current turn; Complete() freezes it permanently.
internal sealed class Turn
{
    private object Sync { get; }
    private List<TurnEvent> EventList { get; } = new();

    internal Turn(
        string prompt, IReadOnlyList<AttachmentInfo> attachments, object sync,
        uint undoRecord = 0, DateTimeOffset? startedAt = null)
    {
        Prompt = prompt;
        Attachments = attachments;
        StartedAt = startedAt ?? DateTimeOffset.UtcNow;
        Sync = sync;
        UndoRecord = undoRecord;
    }

    public string Prompt { get; }
    public IReadOnlyList<AttachmentInfo> Attachments { get; }

    // The document undo record this turn's changes went into; 0 when it has none, which is the case
    // for a restored transcript and for a document with undo recording off. Non-zero is what puts
    // Revert on offer (see TurnRevert).
    public uint UndoRecord { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool Completed { get { lock (Sync) return CompletedAt.HasValue; } }

    // Token/cost accounting from the turn's terminal event; TokenUsage.Empty until the agent
    // reports it (and stays Empty for agents that never do).
    public TokenUsage Usage { get { lock (Sync) return UsageValue; } }
    private TokenUsage UsageValue { get; set; } = TokenUsage.Empty;

    internal void SetUsage(TokenUsage usage) { lock (Sync) UsageValue = usage; }

    public IReadOnlyList<TurnEvent> Events { get { lock (Sync) return EventList.ToArray(); } }

    internal void Add(TurnEvent ev) { lock (Sync) EventList.Add(ev); }
    internal void Complete(DateTimeOffset? at = null) { lock (Sync) CompletedAt ??= at ?? DateTimeOffset.UtcNow; }

    // Fold a tool's output into its originating ToolUse event (matched by id, most-recent first) so
    // it surfaces in that chip's expander rather than as a stray bubble. A missing id is dropped,
    // not turned into a new event.
    internal void SetToolResult(string id, string result, bool failed)
    {
        if (id.Length == 0)
            return;
        lock (Sync)
        {
            for (int i = EventList.Count - 1; i >= 0; i--)
            {
                TurnEvent ev = EventList[i];
                if (ev.Kind == TurnEventKind.ToolUse && ev.Id == id)
                {
                    EventList[i] = ev with { Result = result, Failed = failed, Done = true };
                    return;
                }
            }
        }
    }
}

// One lock guards the whole graph (shared with each Turn): reader thread writes, PromptAsync
// prompts, UI reads. Lifecycle events sit outside turns: a "session started" can arrive
// before the first turn exists.
internal sealed class Conversation
{
    private object Sync { get; } = new();
    private List<Turn> TurnList { get; } = new();
    private List<TurnEvent> LifecycleList { get; } = new();
    private Turn? Current { get; set; }

    // Transient UI state, not transcript history: the panel renders these inline while posed
    // ask_user questions are unanswered. Deliberately kept out of Render() and any persistence so a
    // half-asked question is never serialized. Appended by the ask_user tool body; cleared by the
    // panel once the answer prompt is dispatched. A list because one call can pose several questions
    // and a later call adds to the set rather than replacing it.
    private List<PendingQuestion> PendingQuestionList { get; } = new();

    // Tool calls waiting for the user to allow or refuse them; see TryGetPendingPermissions.
    private List<PermissionRequest> PendingPermissionList { get; } = new();

    public Conversation(Guid agentSessionId, string agentName, string docTitle, AIProfile profile = AIProfile.Rhino)
    {
        AgentSessionId = agentSessionId;
        AgentName = agentName;
        DocTitle = docTitle;
        Profile = profile;
        StartedAt = DateTimeOffset.UtcNow;
    }

    // Rebuild a live conversation from a persisted transcript so a resumed session shows its prior
    // turns and carries the saved AgentSessionId (the CLI's --resume token). The original StartedAt is
    // kept (not reset to now) so the recents ordering and header stay truthful, and the restored turns
    // are marked complete. A malformed SessionId degrades to a fresh GUID so a corrupt store can never
    // throw here.
    public static Conversation Restore(ConversationDto dto)
    {
        Guid sessionId = Guid.TryParse(dto.SessionId, out Guid parsed) ? parsed : Guid.NewGuid();
        Conversation convo = new(sessionId, dto.AgentName, dto.DocTitle, AIProfiles.Parse(dto.Profile))
        {
            StartedAt = dto.StartedAt,
        };

        foreach (TurnEventDto ev in dto.Lifecycle)
            convo.LifecycleList.Add(new TurnEvent(ev.Kind, ev.Text, ev.At, ev.Args, ev.Result, ev.Id, ev.Failed));

        foreach (TurnDto turnDto in dto.Turns)
        {
            Turn turn = new(turnDto.Prompt, turnDto.Attachments ?? [], convo.Sync, startedAt: turnDto.StartedAt);
            foreach (TurnEventDto ev in turnDto.Events)
                // Transcripts saved before Done existed carry it as false, so fall back to the old inference.
                turn.Add(new TurnEvent(ev.Kind, ev.Text, ev.At, ev.Args, ev.Result, ev.Id, ev.Failed,
                    ev.Done || !string.IsNullOrWhiteSpace(ev.Result)));
            turn.SetUsage(turnDto.Usage);
            // Stamping now would date every restored turn to the restore, which is what bounds the search for images the agent wrote during it.
            turn.Complete(turnDto.CompletedAt ?? turnDto.StartedAt);
            convo.TurnList.Add(turn);
        }
        return convo;
    }

    // This is the CLI's own session id, not a plugin-internal handle: it is passed as --session-id
    // on the first spawn and as --resume afterwards, and persisted as ConversationDto.SessionId.
    // Mutable because a rejected resume forces the agent to open a fresh session, and the saved id
    // has to follow or the next resume names a session the CLI has never heard of.
    public Guid AgentSessionId { get; private set; }
    public string AgentName { get; }
    public string DocTitle { get; }
    // Which panel owns this transcript; each panel lists and resumes only its own.
    public AIProfile Profile { get; }
    public DateTimeOffset StartedAt { get; private init; }

    // Raised after every mutation so a panel can re-render. Fired OUTSIDE the lock: handlers
    // marshal to the UI thread and read the graph, which would deadlock if we still held Sync.
    public event Action? Changed;

    // For state a panel renders but the transcript does not hold — whether Rhino has stopped and is
    // waiting for the user, say. Nothing here changed; the panel is only asked to look again.
    public void Touch() => Changed?.Invoke();

    // Live references, not a snapshot: the current turn may still be appending.
    public IReadOnlyList<Turn> Turns { get { lock (Sync) return TurnList.ToArray(); } }
    public IReadOnlyList<TurnEvent> Lifecycle { get { lock (Sync) return LifecycleList.ToArray(); } }

    // The undo record AgentDispatch opened for the turn that is about to begin, consumed by the next
    // BeginTurn. The record has to be recording before the agent's first tool call lands, which is
    // earlier than the turn object exists, so it arrives separately.
    private uint PendingUndoRecord { get; set; }

    public void NoteUndoRecord(uint recordSerial)
    {
        lock (Sync) PendingUndoRecord = recordSerial;
    }

    public Turn BeginTurn(string prompt, IReadOnlyList<AttachmentInfo>? attachments = null)
    {
        Turn turn;
        lock (Sync)
        {
            turn = new(prompt, attachments ?? [], Sync, PendingUndoRecord);
            PendingUndoRecord = 0;
            TurnList.Add(turn);
            Current = turn;
        }
        Changed?.Invoke();
        return turn;
    }

    public void Record(TurnEventKind kind, string text, string args = "", string result = "", string id = "")
    {
        lock (Sync)
            Current?.Add(new TurnEvent(kind, text, DateTimeOffset.UtcNow, args, result, id));
        Changed?.Invoke();
    }

    // Attach a completed tool's output to its originating ToolUse event (see Turn.SetToolResult).
    public void CompleteToolCall(string id, string result, bool failed = false)
    {
        lock (Sync)
            Current?.SetToolResult(id, result, failed);
        Changed?.Invoke();
    }

    // Record the current turn's token/cost accounting (from its terminal event). A no-op once the
    // terminal event already cleared Current (a late, mis-ordered usage drop is dropped, not stored
    // on the wrong turn). Empty usage is ignored so a turn the agent never accounted for stays Empty.
    public void RecordUsage(TokenUsage usage)
    {
        if (usage.IsEmpty)
            return;
        lock (Sync)
            Current?.SetUsage(usage);
        Changed?.Invoke();
    }

    // Sum of every turn's usage: the session total shown in the header. Costs add only when reported
    // (see TokenUsage.operator+), so a tokens-only session yields a null session cost.
    public TokenUsage SessionUsage
    {
        get
        {
            TokenUsage total = TokenUsage.Empty;
            lock (Sync)
                foreach (Turn turn in TurnList)
                    total += turn.Usage;
            return total;
        }
    }

    // Called when the agent had to open a session under a different id than the one it was given.
    internal void AdoptSessionId(Guid sessionId)
    {
        lock (Sync)
            AgentSessionId = sessionId;
    }

    public void NoteSessionStarted()
    {
        lock (Sync)
            LifecycleList.Add(new TurnEvent(TurnEventKind.SessionStarted, "session started", DateTimeOffset.UtcNow));
        Changed?.Invoke();
    }

    // A free-form lifecycle line, rendered like a session marker. Used to surface a fail-soft note
    // (e.g. a stale --resume target the CLI rejected, so the session restarted fresh).
    public void NoteSystem(string text)
    {
        lock (Sync)
            LifecycleList.Add(new TurnEvent(TurnEventKind.SessionStarted, text, DateTimeOffset.UtcNow));
        Changed?.Invoke();
    }

    public bool TryGetPendingQuestions(out IReadOnlyList<PendingQuestion> questions)
    {
        lock (Sync)
        {
            questions = PendingQuestionList.ToArray();
            return questions.Count > 0;
        }
    }

    // Appends: a second ask_user must not evict an earlier unanswered question.
    public void AddPendingQuestions(IReadOnlyList<PendingQuestion> questions)
    {
        lock (Sync)
            foreach (PendingQuestion question in questions)
                PendingQuestionList.Add(question);
        Changed?.Invoke();
    }

    // Removes exactly these instances, so a finished question clearing late can't wipe a newer one
    // posed after it (e.g. a superseding ask_user posed before the old answer landed).
    public void ClearPendingQuestions(IReadOnlyList<PendingQuestion> questions)
    {
        lock (Sync)
            foreach (PendingQuestion question in questions)
                for (int i = PendingQuestionList.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(PendingQuestionList[i], question))
                        PendingQuestionList.RemoveAt(i);
        Changed?.Invoke();
    }

    // A tool set to Ask waits here for its answer, which the panel gives from a card in the chat.
    // One at a time in practice, since the agent's turn blocks on it, but held as a list so a second
    // one cannot silently evict the first.
    public bool TryGetPendingPermissions(out IReadOnlyList<PermissionRequest> permissions)
    {
        lock (Sync)
        {
            permissions = PendingPermissionList.ToArray();
            return permissions.Count > 0;
        }
    }

    public void AddPendingPermission(PermissionRequest permission)
    {
        lock (Sync)
            PendingPermissionList.Add(permission);
        Changed?.Invoke();
    }

    // Removes exactly this instance, so a request withdrawn late cannot wipe a newer one.
    public void RemovePendingPermission(PermissionRequest permission)
    {
        lock (Sync)
            for (int i = PendingPermissionList.Count - 1; i >= 0; i--)
                if (ReferenceEquals(PendingPermissionList[i], permission))
                    PendingPermissionList.RemoveAt(i);
        Changed?.Invoke();
    }

    public void CompleteTurn()
    {
        lock (Sync)
        {
            Current?.Complete();
            Current = null;   // stray late events after a terminal event are dropped, not mis-filed
        }
        Changed?.Invoke();
    }

    // Flatten to plain text; assistant chunks appended raw to rejoin the stream.
    public string Render()
    {
        StringBuilder sb = new();
        lock (Sync)
        {
            foreach (TurnEvent ev in LifecycleList)
                sb.AppendLine($"[{ev.Text}]");

            foreach (Turn turn in TurnList)
            {
                sb.AppendLine();
                sb.AppendLine($"> {turn.Prompt}");
                foreach (TurnEvent ev in turn.Events)
                {
                    switch (ev.Kind)
                    {
                        case TurnEventKind.ToolUse:
                            sb.AppendLine($"  ⚙ {ev.Text}");
                            break;
                        case TurnEventKind.Result:
                            sb.AppendLine();
                            sb.AppendLine(ev.Text);
                            break;
                        default:
                            sb.Append(ev.Text);
                            break;
                    }
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }
}
