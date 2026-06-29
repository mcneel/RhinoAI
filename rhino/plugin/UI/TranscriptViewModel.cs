using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RhMcp;

internal enum TranscriptRole
{
    System,
    User,
    Agent,
    Tool,
}

// One rendered row in the transcript. Dumb, immutable. Tool rows carry the call's Args/Result for the
// expander plus a Summary (the human-readable chip header); bubble/system rows leave them empty. A
// bubble row carries its Timestamp (for the hover meta's "time since") and, on the turn's last bubble,
// that turn's TokenUsage; every other role leaves Usage Empty. Record-struct equality (TokenUsage is
// itself a record struct) keeps ReconcileItems diffing exact.
internal readonly record struct TranscriptItem(
    TranscriptRole Role,
    string Text,
    string ToolArgs = "",
    string ToolResult = "",
    string Summary = "",
    TokenUsage Usage = default,
    DateTimeOffset Timestamp = default);

// Flattens a live Conversation or a persisted ConversationDto into the ordered rows the panel
// renders. Assistant text chunks are coalesced into one bubble per run, and a tool call collapses
// into a single chip carrying its args + result, so raw tool JSON never lands in a bubble. The
// same shaping serves both the live and read-only views.
internal sealed class TranscriptViewModel
{
    public IReadOnlyList<TranscriptItem> Items { get; }
    public bool Running { get; }

    private TranscriptViewModel(IReadOnlyList<TranscriptItem> items, bool running)
    {
        Items = items;
        Running = running;
    }

    public static TranscriptViewModel FromLive(Conversation convo)
    {
        List<TranscriptItem> items = [];
        foreach (TurnEvent ev in convo.Lifecycle)
            items.Add(new TranscriptItem(TranscriptRole.System, ev.Text));

        bool running = false;
        foreach (Turn turn in convo.Turns)
        {
            AppendTurn(
                items,
                turn.Prompt,
                turn.StartedAt,
                turn.Events.Select(static ev => (ev.Kind, ev.Text, ev.Args, ev.Result, ev.At)),
                turn.Completed,
                turn.Usage);
            running = !turn.Completed;
        }
        return new TranscriptViewModel(items, running);
    }

    public static TranscriptViewModel FromReview(ConversationDto convo)
    {
        List<TranscriptItem> items = [];
        foreach (TurnEventDto ev in convo.Lifecycle)
            items.Add(new TranscriptItem(TranscriptRole.System, ev.Text));

        foreach (TurnDto turn in convo.Turns)
            AppendTurn(
                items,
                turn.Prompt,
                turn.StartedAt,
                turn.Events.Select(static ev => (ev.Kind, ev.Text, ev.Args, ev.Result, ev.At)),
                completed: true,   // persisted turns are always complete
                turn.Usage);
        return new TranscriptViewModel(items, running: false);
    }

    // One turn → its user prompt, the flattened agent/tool rows, and (when the turn completed with a
    // figure) the turn's token usage folded onto its last bubble. Tokens are per-turn, not per-message,
    // so they land on the turn's last user/agent bubble (the final reply, or the prompt if the turn
    // produced no agent text) rather than as a separate row.
    private static void AppendTurn(
        List<TranscriptItem> items,
        string prompt,
        DateTimeOffset startedAt,
        IEnumerable<(TurnEventKind Kind, string Text, string Args, string Result, DateTimeOffset At)> events,
        bool completed,
        TokenUsage usage)
    {
        int from = items.Count;
        items.Add(new TranscriptItem(TranscriptRole.User, prompt, Timestamp: startedAt));
        Flatten(items, events);

        if (!completed || usage.IsEmpty)
            return;
        for (int i = items.Count - 1; i >= from; i--)
        {
            if (items[i].Role is TranscriptRole.User or TranscriptRole.Agent)
            {
                items[i] = items[i] with { Usage = usage };
                return;
            }
        }
    }

    private static void Flatten(
        List<TranscriptItem> items,
        IEnumerable<(TurnEventKind Kind, string Text, string Args, string Result, DateTimeOffset At)> events)
    {
        StringBuilder assistant = new();
        DateTimeOffset runStart = default;
        void FlushAssistant()
        {
            if (assistant.Length == 0)
                return;
            items.Add(new TranscriptItem(TranscriptRole.Agent, assistant.ToString(), Timestamp: runStart));
            assistant.Clear();
        }

        foreach ((TurnEventKind Kind, string Text, string Args, string Result, DateTimeOffset At) ev in events)
        {
            switch (ev.Kind)
            {
                case TurnEventKind.AssistantText:
                    if (assistant.Length == 0)
                        runStart = ev.At;   // first chunk stamps the coalesced run, stable across deltas
                    assistant.Append(ev.Text);
                    break;
                case TurnEventKind.ToolUse:
                    FlushAssistant();
                    items.Add(new TranscriptItem(
                        TranscriptRole.Tool,
                        ev.Text,
                        ev.Args,
                        ev.Result,
                        Summary: ToolSummary.Describe(ev.Text, ev.Args, ev.Result),
                        Timestamp: ev.At));
                    break;
                case TurnEventKind.Result:
                    FlushAssistant();
                    if (!string.IsNullOrWhiteSpace(ev.Text))
                        items.Add(new TranscriptItem(TranscriptRole.Agent, ev.Text, Timestamp: ev.At));
                    break;
            }
        }
        FlushAssistant();
    }
}
