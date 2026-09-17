using NUnit.Framework;
using Rhino.AI.UI;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public class ConversationFeedTests
{
    [Test]
    public void A_silent_completion_is_reported_ok_not_running()
    {
        List<PanelEvent> emitted = [];
        Conversation convo = new(Guid.NewGuid(), "claude", "doc.3dm");
        ConversationFeed feed = new(convo, emitted.Add);

        convo.BeginTurn("go");
        convo.Record(TurnEventKind.ToolUse, "list_objects", id: "c1");
        feed.Pump();
        Assert.That(StatusOf(emitted, "c1"), Is.EqualTo("running"));

        convo.CompleteToolCall("c1", string.Empty);
        feed.Pump();
        Assert.That(StatusOf(emitted, "c1"), Is.EqualTo("ok"));
    }

    [TearDown]
    public void ClearActivity() => ToolActivity.Waiting = null;

    [Test]
    public void A_call_still_running_is_not_reported_in_the_past_tense()
    {
        List<PanelEvent> emitted = [];
        Conversation convo = new(Guid.NewGuid(), "claude", "doc.3dm");
        ConversationFeed feed = new(convo, emitted.Add);

        convo.BeginTurn("go");
        convo.Record(TurnEventKind.ToolUse, "script_editor_run", id: "c1");
        feed.Pump();
        Assert.That(TitleOf(emitted, "c1"), Is.EqualTo("running the script"));

        convo.CompleteToolCall("c1", "{\"stdout\":\"\"}");
        feed.Pump();
        Assert.That(TitleOf(emitted, "c1"), Is.EqualTo("ran the script"));
    }

    // The script stopped at a getter: the card has to say so while it lasts, and stop saying it after.
    [Test]
    public void A_call_waiting_on_the_user_says_what_Rhino_is_asking()
    {
        List<PanelEvent> emitted = [];
        Conversation convo = new(Guid.NewGuid(), "claude", "doc.3dm");
        ConversationFeed feed = new(convo, emitted.Add);

        convo.BeginTurn("go");
        convo.Record(TurnEventKind.ToolUse, "script_editor_run", id: "c1");
        feed.Pump();

        ToolActivity.Waiting = "waiting for you — Select curves to loft";
        feed.Pump();
        Assert.That(TitleOf(emitted, "c1"), Is.EqualTo("waiting for you — Select curves to loft"));
        Assert.That(PatchesFor(emitted, "c1")[^1].Status, Is.Null, "a live rename must not close the call");

        ToolActivity.Waiting = null;
        feed.Pump();
        Assert.That(TitleOf(emitted, "c1"), Is.EqualTo("running the script"));
    }

    [Test]
    public void A_failure_with_no_output_is_still_a_failure()
    {
        List<PanelEvent> emitted = [];
        Conversation convo = new(Guid.NewGuid(), "claude", "doc.3dm");
        ConversationFeed feed = new(convo, emitted.Add);

        convo.BeginTurn("go");
        convo.Record(TurnEventKind.ToolUse, "run_python", id: "c1");
        feed.Pump();
        convo.CompleteToolCall("c1", string.Empty, failed: true);
        feed.Pump();

        Assert.That(StatusOf(emitted, "c1"), Is.EqualTo("failed"));
        Assert.That(TitleOf(emitted, "c1"), Does.Contain("failed"));
    }

    [Test]
    public void An_open_call_is_unknown_once_the_turn_ends()
    {
        List<PanelEvent> emitted = [];
        Conversation convo = new(Guid.NewGuid(), "claude", "doc.3dm");
        ConversationFeed feed = new(convo, emitted.Add);

        convo.BeginTurn("go");
        convo.Record(TurnEventKind.ToolUse, "run_command", id: "c1");
        feed.Pump();
        Assert.That(feed.IsCallRunning("c1"), Is.True);

        convo.CompleteTurn();
        feed.Pump();
        feed.Pump();

        Assert.That(StatusOf(emitted, "c1"), Is.EqualTo("unknown"));
        Assert.That(PatchesFor(emitted, "c1"), Has.Count.EqualTo(1));
        Assert.That(PatchesFor(emitted, "c1")[0].Chips, Is.Empty);
        Assert.That(feed.IsCallRunning("c1"), Is.False);
    }

    // Conversation drops events once its terminal event has landed, so unknown is the final word.
    [Test]
    public void A_result_landing_after_the_turn_ended_does_not_revive_the_call()
    {
        List<PanelEvent> emitted = [];
        Conversation convo = new(Guid.NewGuid(), "claude", "doc.3dm");
        ConversationFeed feed = new(convo, emitted.Add);

        convo.BeginTurn("go");
        convo.Record(TurnEventKind.ToolUse, "list_objects", id: "c1");
        feed.Pump();
        convo.CompleteTurn();
        feed.Pump();
        convo.CompleteToolCall("c1", "{\"count\":3}");
        feed.Pump();

        Assert.That(StatusOf(emitted, "c1"), Is.EqualTo("unknown"));
    }

    [Test]
    public void A_transcript_saved_before_Done_existed_still_reports_its_tools_finished()
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        TurnEventDto tool = new(TurnEventKind.ToolUse, "list_objects", at, "{}", "{\"Ok\":true,\"count\":3}", "c1");
        ConversationDto dto = new("2f8b1c40-5d3e-4a91-9c62-7f0a8e14d5b3", "claude", "doc.3dm", at, [],
            [new TurnDto("go", at, at, [tool])]);

        List<PanelEvent> emitted = [];
        ConversationFeed feed = new(Conversation.Restore(dto), emitted.Add);
        feed.Replay(readOnly: true);

        Assert.That(StatusOf(emitted, "c1"), Is.EqualTo("ok"));
    }

    [Test]
    public void A_sent_turn_names_the_files_that_went_with_it()
    {
        List<PanelEvent> emitted = [];
        Conversation convo = new(Guid.NewGuid(), "claude", "doc.3dm");
        ConversationFeed feed = new(convo, emitted.Add);

        convo.BeginTurn(string.Empty, [new AttachmentInfo(AttachmentKind.Image, "plan.png", "image/png", 68)]);
        feed.Pump();

        PanelTurn begun = emitted.OfType<TurnBeginEvent>().Single().Turn;
        Assert.That(begun.Attachments, Has.Count.EqualTo(1));
        Assert.That(begun.Attachments[0].Name, Is.EqualTo("plan.png"));
        Assert.That(begun.Attachments[0].Kind, Is.EqualTo("image"));
        Assert.That(begun.Attachments[0].Bytes, Is.EqualTo(68));
        Assert.That(begun.Attachments[0].DataUrl, Is.Null);
    }

    [Test]
    public void A_reloaded_conversation_still_names_them()
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        AttachmentInfo image = new(AttachmentKind.Image, "plan.png", "image/png", 68);
        ConversationDto dto = new("2f8b1c40-5d3e-4a91-9c62-7f0a8e14d5b3", "claude", "doc.3dm", at, [],
            [new TurnDto("what is this", at, at, [], default, [image])]);

        List<PanelEvent> emitted = [];
        ConversationFeed feed = new(Conversation.Restore(dto), emitted.Add);
        feed.Replay(readOnly: true);

        PanelTurn begun = emitted.OfType<TurnBeginEvent>().Single().Turn;
        Assert.That(begun.Attachments.Select(static a => a.Name), Is.EqualTo(new[] { "plan.png" }));
    }

    [Test]
    public void An_attachment_survives_the_trip_through_the_saved_transcript()
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        AttachmentInfo image = new(AttachmentKind.Image, "plan.png", "image/png", 68);
        ConversationDto saved = new("2f8b1c40-5d3e-4a91-9c62-7f0a8e14d5b3", "claude", "doc.3dm", at, [],
            [new TurnDto("what is this", at, at, [], default, [image])]);

        string json = JsonSerializer.Serialize(saved, McpSerializer.Options);
        ConversationDto? reloaded = JsonSerializer.Deserialize<ConversationDto>(json, McpSerializer.Options);

        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.Turns[0].Attachments, Is.EqualTo(new[] { image }));
    }

    [Test]
    public void A_transcript_saved_before_attachments_existed_reloads_with_none()
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        ConversationDto dto = new("2f8b1c40-5d3e-4a91-9c62-7f0a8e14d5b3", "claude", "doc.3dm", at, [],
            [new TurnDto("go", at, at, [])]);

        List<PanelEvent> emitted = [];
        ConversationFeed feed = new(Conversation.Restore(dto), emitted.Add);
        feed.Replay(readOnly: true);

        Assert.That(emitted.OfType<TurnBeginEvent>().Single().Turn.Attachments, Is.Empty);
    }

    private static string? StatusOf(IReadOnlyList<PanelEvent> emitted, string callId)
    {
        string? status = null;
        foreach (PanelEvent ev in emitted)
            switch (ev)
            {
                case TurnToolEvent call when call.Call.Id == callId:
                    status = call.Call.Status;
                    break;
                case TurnToolPatchEvent patch when patch.CallId == callId && patch.Patch.Status is string landed:
                    status = landed;
                    break;
            }
        return status;
    }

    private static string? TitleOf(IReadOnlyList<PanelEvent> emitted, string callId)
    {
        string? title = null;
        foreach (PanelEvent ev in emitted)
            switch (ev)
            {
                case TurnToolEvent call when call.Call.Id == callId:
                    title = call.Call.Title;
                    break;
                case TurnToolPatchEvent patch when patch.CallId == callId && patch.Patch.Title is string landed:
                    title = landed;
                    break;
            }
        return title;
    }

    private static List<PanelToolPatch> PatchesFor(IReadOnlyList<PanelEvent> emitted, string callId)
    {
        List<PanelToolPatch> patches = [];
        foreach (PanelEvent ev in emitted)
            if (ev is TurnToolPatchEvent patch && patch.CallId == callId)
                patches.Add(patch.Patch);
        return patches;
    }
}
