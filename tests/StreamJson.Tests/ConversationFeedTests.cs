using System.Text.Json;

using RhinoAI.WebPanel;

namespace RhinoAI.StreamJson.Tests;

// ConversationFeed is the only thing standing between "something changed" and the panel's delta
// stream, so these pin the two properties the panel depends on: consecutive assistant chunks
// coalesce into one block, and a tool result that folds into an already-reported call arrives as a
// patch rather than a second call.
[TestFixture]
public sealed class ConversationFeedTests
{
    private Conversation Convo { get; set; } = null!;
    private List<PanelEvent> Sent { get; set; } = null!;
    private ConversationFeed Feed { get; set; } = null!;

    [SetUp]
    public void SetUp()
    {
        Convo = new Conversation(Guid.NewGuid(), "claude", "tower.3dm");
        Sent = [];
        Feed = new ConversationFeed(Convo, Sent.Add);
    }

    private T[] OfType<T>() => Sent.OfType<T>().ToArray();

    [Test]
    public void Replay_opens_with_a_session_then_streams_the_turns_as_deltas()
    {
        Convo.BeginTurn("draw a box");
        Convo.Record(TurnEventKind.AssistantText, "Sure, ");

        Feed.Replay();

        Assert.That(Sent[0], Is.TypeOf<ConversationEvent>());
        Assert.That(((ConversationEvent)Sent[0]).Snapshot.Turns, Is.Empty,
            "turns arrive as events, so the snapshot only establishes the session");
        Assert.That(OfType<TurnBeginEvent>(), Has.Length.EqualTo(1));
        Assert.That(OfType<TurnTextEvent>().Single().Delta, Is.EqualTo("Sure, "));
    }

    [Test]
    public void Consecutive_assistant_chunks_extend_one_block()
    {
        Convo.BeginTurn("hi");
        Feed.Replay();
        Sent.Clear();

        Convo.Record(TurnEventKind.AssistantText, "one ");
        Convo.Record(TurnEventKind.AssistantText, "two ");
        Convo.Record(TurnEventKind.AssistantText, "three");
        Feed.Pump();

        TurnTextEvent[] text = OfType<TurnTextEvent>();
        Assert.That(text.Select(t => t.Delta), Is.EqualTo(new[] { "one ", "two ", "three" }));
        Assert.That(text.Select(t => t.BlockId).Distinct().Count(), Is.EqualTo(1),
            "the panel appends a delta to the block it names, so the id must not change mid-run");
    }

    [Test]
    public void A_tool_call_closes_the_open_block_and_later_text_opens_a_new_one()
    {
        Convo.BeginTurn("hi");
        Feed.Replay();
        Sent.Clear();

        Convo.Record(TurnEventKind.AssistantText, "before");
        Convo.Record(TurnEventKind.ToolUse, "run_python", "{\"code\":\"1\"}", string.Empty, "call-1");
        Convo.Record(TurnEventKind.AssistantText, "after");
        Feed.Pump();

        string[] blocks = OfType<TurnTextEvent>().Select(t => t.BlockId).ToArray();
        Assert.That(blocks, Has.Length.EqualTo(2));
        Assert.That(blocks[0], Is.Not.EqualTo(blocks[1]));
        Assert.That(OfType<TurnToolEvent>().Single().Call.Status, Is.EqualTo("running"),
            "a call with no result yet is still in flight");
    }

    [Test]
    public void A_result_folding_into_a_reported_call_arrives_as_a_patch()
    {
        Convo.BeginTurn("hi");
        Convo.Record(TurnEventKind.ToolUse, "run_python", "{}", string.Empty, "call-1");
        Feed.Replay();
        Sent.Clear();

        Convo.CompleteToolCall("call-1", "{\"Ok\":true,\"lines\":3}");
        Feed.Pump();

        Assert.That(OfType<TurnToolEvent>(), Is.Empty, "the call was already reported");
        TurnToolPatchEvent patch = OfType<TurnToolPatchEvent>().Single();
        Assert.That(patch.CallId, Is.EqualTo("call-1"));
        Assert.That(patch.Patch.Status, Is.EqualTo("ok"));
        Assert.That(patch.Patch.Result?.ToJsonString(), Does.Contain("\"lines\":3"));
    }

    [Test]
    public void A_failed_result_carries_the_tools_own_message()
    {
        Convo.BeginTurn("hi");
        Convo.Record(TurnEventKind.ToolUse, "run_command", "{}", string.Empty, "call-1");
        Feed.Replay();
        Sent.Clear();

        Convo.CompleteToolCall("call-1", "{\"Ok\":false,\"error\":\"nothing selected\"}");
        Feed.Pump();

        PanelToolPatch patch = OfType<TurnToolPatchEvent>().Single().Patch;
        Assert.That(patch.Status, Is.EqualTo("failed"));
        Assert.That(patch.Error, Is.EqualTo("nothing selected"));
    }

    // A plain-text CLI error carries none of the shapes IsFailure looks for, so it used to read "ok".
    [Test]
    public void A_result_the_agent_flagged_as_an_error_is_failed_even_without_an_error_shape()
    {
        Convo.BeginTurn("hi");
        Convo.Record(TurnEventKind.ToolUse, "run_python", "{}", string.Empty, "call-1");
        Feed.Replay();
        Sent.Clear();

        Convo.CompleteToolCall("call-1", "\"NameError: name 'boom' is not defined\"", failed: true);
        Feed.Pump();

        PanelToolPatch patch = OfType<TurnToolPatchEvent>().Single().Patch;
        Assert.That(patch.Status, Is.EqualTo("failed"));
        Assert.That(patch.Title, Is.EqualTo("python failed"));
    }

    [Test]
    public void Nothing_is_re_emitted_when_nothing_changed()
    {
        Convo.BeginTurn("hi");
        Convo.Record(TurnEventKind.AssistantText, "done");
        Feed.Replay();
        Sent.Clear();

        Feed.Pump();
        Feed.Pump();

        Assert.That(Sent, Is.Empty, "an idle Changed must not replay the transcript");
    }

    [Test]
    public void Usage_and_completion_report_once()
    {
        Convo.BeginTurn("hi");
        Feed.Replay();
        Sent.Clear();

        Convo.RecordUsage(new TokenUsage(120, 30, 0.02m));
        Convo.CompleteTurn();
        Feed.Pump();
        Feed.Pump();

        Assert.That(OfType<TurnUsageEvent>().Single().Usage.InputTokens, Is.EqualTo(120));
        Assert.That(OfType<TurnEndEvent>(), Has.Length.EqualTo(1));
    }

    [Test]
    public void A_pending_question_is_posed_once_and_cleared_when_answered()
    {
        Convo.BeginTurn("hi");
        Feed.Replay();
        Sent.Clear();

        PendingQuestion question = new("Which?", ["a", "b"], AskUserMode.Single);
        Convo.SetPendingQuestion(question);
        Feed.Pump();
        Feed.Pump();

        QuestionEvent posed = OfType<QuestionEvent>().Single();
        Assert.That(posed.Question.Options, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(Feed.TryResolveQuestion(posed.Question.Id, out PendingQuestion resolved), Is.True);
        Assert.That(resolved, Is.SameAs(question), "an answer has to reach the instance the picker arbitrates on");

        Convo.ClearPendingQuestion(question);
        Feed.Pump();
        Assert.That(OfType<QuestionClearEvent>().Single().Id, Is.EqualTo(posed.Question.Id));
    }

    // The panel reads `type` to pick a case, camelCases its fields, and treats an absent optional
    // differently from a null one, so these three are part of the contract, not formatting taste.
    [Test]
    public void Serialization_matches_what_the_panel_parses()
    {
        string json = PanelJson.Serialize(new TurnTextEvent("turn-0", "turn-0-b1", "hello"));

        Assert.That(json, Does.StartWith("{\"type\":\"turn.text\""));
        Assert.That(json, Does.Contain("\"turnId\":\"turn-0\""));
        Assert.That(json, Does.Contain("\"blockId\":\"turn-0-b1\""));
    }

    [Test]
    public void An_unset_optional_is_absent_rather_than_null()
    {
        Convo.BeginTurn("hi");
        Convo.Record(TurnEventKind.ToolUse, "run_python", "{}", string.Empty, "call-1");
        Feed.Replay();

        string json = PanelJson.Serialize(OfType<TurnToolEvent>().Single());

        Assert.That(json, Does.Not.Contain("durationMs"),
            "the panel renders a present-but-null duration as 0ms");
        Assert.That(json, Does.Not.Contain("\"error\""));
    }

    [Test]
    public void Commands_round_trip_from_the_panels_wire_format()
    {
        PanelCommand? prompt = PanelJson.Deserialize("{\"type\":\"prompt\",\"request\":{\"text\":\"draw a box\"}}");
        Assert.That(prompt, Is.TypeOf<PromptCommand>());
        Assert.That(((PromptCommand)prompt!).Request.Text, Is.EqualTo("draw a box"));

        PanelCommand? answer = PanelJson.Deserialize("{\"type\":\"question.answer\",\"id\":\"q1\",\"answers\":[\"a\"]}");
        Assert.That(((AnswerQuestionCommand)answer!).Answers, Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void A_command_this_build_does_not_implement_throws_rather_than_being_mistaken_for_another()
    {
        // PanelBridge catches this and logs; the point is that it never silently decodes as a
        // different command, which would be far worse than ignoring it.
        Assert.That(() => PanelJson.Deserialize("{\"type\":\"turn.undo\",\"turnId\":\"turn-0\"}"),
            Throws.InstanceOf<JsonException>().Or.InstanceOf<NotSupportedException>());
    }
}
