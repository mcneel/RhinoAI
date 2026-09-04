using System.Threading;
using System.Threading.Tasks;
using RhinoAI;

namespace RhinoAI.StreamJson.Tests;

// The live transcript graph: turn/event recording, CompleteToolCall folding a tool's output back
// into its originating ToolUse event, the Changed signal firing on every mutation, late/stray-event
// dropping, and the Record path's thread-safety under concurrent writers. Token accounting is
// covered separately in ConversationUsageTests.
[TestFixture]
public sealed class ConversationTests
{
    private static Conversation NewConversation() => new(Guid.NewGuid(), "claude", "Untitled");

    [Test]
    public void Recorded_events_land_on_the_current_turn_in_order()
    {
        Conversation convo = NewConversation();
        convo.BeginTurn("hi");
        convo.Record(TurnEventKind.AssistantText, "one");
        convo.Record(TurnEventKind.AssistantText, "two");

        IReadOnlyList<TurnEvent> events = convo.Turns[0].Events;
        Assert.That(events.Select(static e => e.Text), Is.EqualTo(new[] { "one", "two" }));
    }

    [Test]
    public void CompleteToolCall_folds_the_result_into_its_originating_tool_use_by_id()
    {
        Conversation convo = NewConversation();
        convo.BeginTurn("hi");
        convo.Record(TurnEventKind.ToolUse, "add_box", args: "{\"size\":1}", id: "toolu_1");
        convo.Record(TurnEventKind.AssistantText, "between");
        convo.Record(TurnEventKind.ToolUse, "add_sphere", args: "{}", id: "toolu_2");

        convo.CompleteToolCall("toolu_1", "made a box");

        IReadOnlyList<TurnEvent> events = convo.Turns[0].Events;
        Assert.That(events, Has.Count.EqualTo(3)); // folding never adds a stray event
        Assert.That(events[0].Id, Is.EqualTo("toolu_1"));
        Assert.That(events[0].Result, Is.EqualTo("made a box"));
        Assert.That(events[2].Result, Is.Empty); // the other tool call is untouched
    }

    [Test]
    public void CompleteToolCall_with_unknown_id_is_a_no_op()
    {
        Conversation convo = NewConversation();
        convo.BeginTurn("hi");
        convo.Record(TurnEventKind.ToolUse, "add_box", id: "toolu_1");

        convo.CompleteToolCall("missing", "orphan result");

        Assert.That(convo.Turns[0].Events[0].Result, Is.Empty);
    }

    [Test]
    public void CompleteToolCall_matches_the_most_recent_tool_use_when_ids_repeat()
    {
        Conversation convo = NewConversation();
        convo.BeginTurn("hi");
        convo.Record(TurnEventKind.ToolUse, "first", id: "dup");
        convo.Record(TurnEventKind.ToolUse, "second", id: "dup");

        convo.CompleteToolCall("dup", "for the latest");

        IReadOnlyList<TurnEvent> events = convo.Turns[0].Events;
        Assert.That(events[0].Result, Is.Empty);
        Assert.That(events[1].Result, Is.EqualTo("for the latest"));
    }

    [Test]
    public void Record_after_CompleteTurn_is_dropped_not_misfiled()
    {
        Conversation convo = NewConversation();
        convo.BeginTurn("hi");
        convo.Record(TurnEventKind.AssistantText, "in turn");
        convo.CompleteTurn();

        convo.Record(TurnEventKind.AssistantText, "stray"); // no current turn to attach to

        Assert.That(convo.Turns[0].Events, Has.Count.EqualTo(1));
        Assert.That(convo.Turns[0].Completed, Is.True);
    }

    [Test]
    public void NoteSessionStarted_records_a_lifecycle_event_outside_any_turn()
    {
        Conversation convo = NewConversation();
        convo.NoteSessionStarted();

        Assert.That(convo.Turns, Is.Empty);
        Assert.That(convo.Lifecycle, Has.Count.EqualTo(1));
        Assert.That(convo.Lifecycle[0].Kind, Is.EqualTo(TurnEventKind.SessionStarted));
    }

    [Test]
    public void Changed_fires_on_every_mutation()
    {
        Conversation convo = NewConversation();
        int fired = 0;
        convo.Changed += () => Interlocked.Increment(ref fired);

        convo.NoteSessionStarted();        // 1
        convo.BeginTurn("hi");             // 2
        convo.Record(TurnEventKind.AssistantText, "x"); // 3
        convo.CompleteToolCall("none", "y");            // 4 (no-op fold still signals a render pass)
        convo.RecordUsage(new TokenUsage(1, 1));        // 5
        convo.CompleteTurn();              // 6

        Assert.That(fired, Is.EqualTo(6));
    }

    [Test]
    public void Pending_questions_accumulate_and_clear_reference_guarded()
    {
        Conversation convo = NewConversation();
        Assert.That(convo.TryGetPendingQuestions(out _), Is.False);

        PendingQuestion first = new("pick one", ["a", "b"], AskUserMode.Single);
        convo.AddPendingQuestions([first]);
        Assert.That(convo.TryGetPendingQuestions(out IReadOnlyList<PendingQuestion> got), Is.True);
        Assert.That(got, Is.EqualTo(new[] { first }));

        // A second ask_user APPENDS: an earlier unanswered question is never silently evicted.
        PendingQuestion second = new("again", ["c"], AskUserMode.Multi);
        convo.AddPendingQuestions([second]);
        Assert.That(convo.TryGetPendingQuestions(out IReadOnlyList<PendingQuestion> both), Is.True);
        Assert.That(both, Is.EqualTo(new[] { first, second }), "order is the order they were posed in");

        // A stale clear takes out only its own instance, never a question posed after it.
        convo.ClearPendingQuestions([first]);
        Assert.That(convo.TryGetPendingQuestions(out IReadOnlyList<PendingQuestion> still), Is.True);
        Assert.That(still, Is.EqualTo(new[] { second }));

        convo.ClearPendingQuestions([second]);
        Assert.That(convo.TryGetPendingQuestions(out _), Is.False);
    }

    [Test]
    public void A_batch_of_questions_is_posed_and_cleared_as_one()
    {
        Conversation convo = NewConversation();
        PendingQuestion units = new("units?", ["mm", "m"], AskUserMode.Single);
        PendingQuestion tolerance = new("tolerance?", ["0.001", "0.01"], AskUserMode.Single);

        convo.AddPendingQuestions([units, tolerance]);
        Assert.That(convo.TryGetPendingQuestions(out IReadOnlyList<PendingQuestion> posed), Is.True);
        Assert.That(posed, Is.EqualTo(new[] { units, tolerance }));

        convo.ClearPendingQuestions(posed);
        Assert.That(convo.TryGetPendingQuestions(out _), Is.False);
    }

    [Test]
    public void Concurrent_Record_writers_lose_no_events()
    {
        Conversation convo = NewConversation();
        convo.BeginTurn("stress");

        const int writers = 8;
        const int perWriter = 200;
        Parallel.For(0, writers, w =>
        {
            for (int i = 0; i < perWriter; i++)
                convo.Record(TurnEventKind.AssistantText, $"{w}:{i}");
        });

        Assert.That(convo.Turns[0].Events, Has.Count.EqualTo(writers * perWriter));
    }
}
