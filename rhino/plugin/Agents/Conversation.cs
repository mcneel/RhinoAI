using System.Text;

namespace RhMcp;

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
internal sealed record TurnEvent(
    TurnEventKind Kind,
    string Text,
    DateTimeOffset At,
    string Args = "",
    string Result = "",
    string Id = "");

// Mutated only while it is the current turn; Complete() freezes it permanently.
internal sealed class Turn
{
    private object Sync { get; }
    private List<TurnEvent> EventList { get; } = new();

    internal Turn(string prompt, object sync)
    {
        Prompt = prompt;
        StartedAt = DateTimeOffset.UtcNow;
        Sync = sync;
    }

    public string Prompt { get; }
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
    internal void Complete() { lock (Sync) CompletedAt ??= DateTimeOffset.UtcNow; }

    // Fold a tool's output into its originating ToolUse event (matched by id, most-recent first) so
    // it surfaces in that chip's expander rather than as a stray bubble. A missing id is dropped,
    // not turned into a new event.
    internal void SetToolResult(string id, string result)
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
                    EventList[i] = ev with { Result = result };
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

    // Transient UI state, not transcript history: the panel renders this inline while a posed
    // ask_user question is unanswered. Deliberately kept out of Render() and any persistence so a
    // half-asked question is never serialized. Set by the ask_user tool body; cleared by the panel
    // once the answer prompt is dispatched.
    private PendingQuestion? CurrentQuestion { get; set; }

    public Conversation(Guid agentSessionId, string agentName, string docTitle)
    {
        AgentSessionId = agentSessionId;
        AgentName = agentName;
        DocTitle = docTitle;
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
        Conversation convo = new(sessionId, dto.AgentName, dto.DocTitle)
        {
            StartedAt = dto.StartedAt,
        };

        foreach (TurnEventDto ev in dto.Lifecycle)
            convo.LifecycleList.Add(new TurnEvent(ev.Kind, ev.Text, ev.At, ev.Args, ev.Result, ev.Id));

        foreach (TurnDto turnDto in dto.Turns)
        {
            Turn turn = new(turnDto.Prompt, convo.Sync);
            foreach (TurnEventDto ev in turnDto.Events)
                turn.Add(new TurnEvent(ev.Kind, ev.Text, ev.At, ev.Args, ev.Result, ev.Id));
            turn.SetUsage(turnDto.Usage);
            turn.Complete();
            convo.TurnList.Add(turn);
        }
        return convo;
    }

    public Guid AgentSessionId { get; }
    public string AgentName { get; }
    public string DocTitle { get; }
    public DateTimeOffset StartedAt { get; private init; }

    // Raised after every mutation so a panel can re-render. Fired OUTSIDE the lock: handlers
    // marshal to the UI thread and read the graph, which would deadlock if we still held Sync.
    public event Action? Changed;

    // Live references, not a snapshot: the current turn may still be appending.
    public IReadOnlyList<Turn> Turns { get { lock (Sync) return TurnList.ToArray(); } }
    public IReadOnlyList<TurnEvent> Lifecycle { get { lock (Sync) return LifecycleList.ToArray(); } }

    public Turn BeginTurn(string prompt)
    {
        Turn turn;
        lock (Sync)
        {
            turn = new(prompt, Sync);
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
    public void CompleteToolCall(string id, string result)
    {
        lock (Sync)
            Current?.SetToolResult(id, result);
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

    public bool TryGetPendingQuestion(out PendingQuestion question)
    {
        lock (Sync)
        {
            question = CurrentQuestion!;
            return CurrentQuestion is not null;
        }
    }

    public void SetPendingQuestion(PendingQuestion question)
    {
        lock (Sync)
            CurrentQuestion = question;
        Changed?.Invoke();
    }

    // ReferenceEquals-guarded so a finished question clearing late can't wipe a newer one that
    // already replaced it (e.g. a superseding ask_user posed before the old answer landed).
    public void ClearPendingQuestion(PendingQuestion question)
    {
        lock (Sync)
            if (ReferenceEquals(CurrentQuestion, question))
                CurrentQuestion = null;
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
