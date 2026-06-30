using RhMcp;

namespace RhMcp.StreamJson.Tests;

// Guards the headline serialization trap of the SessionId -> AgentSessionId rename: the live
// Conversation property was renamed, but the persisted ConversationDto.SessionId field/key MUST
// stay 'sessionId' or every saved transcript orphans. Save then List/TryLoad must round-trip, and
// an existing on-disk transcript (camelCase 'sessionId') must still deserialize.
[TestFixture]
public sealed class ConversationStoreRoundTripTests
{
    [SetUp]
    public void Reset() => AISettings.ResetForTest();

    [Test]
    public void Save_then_List_and_TryLoad_round_trips_the_transcript()
    {
        Guid sessionId = Guid.NewGuid();
        Conversation conversation = new(sessionId, "claude", "MyDoc.3dm");
        conversation.NoteSessionStarted();
        conversation.BeginTurn("hello");
        conversation.Record(TurnEventKind.AssistantText, "hi");
        conversation.Record(TurnEventKind.ToolUse, "add_box", args: "{\"size\":1}", id: "toolu_1");
        conversation.CompleteToolCall("toolu_1", "created");
        conversation.CompleteTurn();

        ConversationStore.Save(conversation);

        // The persisted key is the live AgentSessionId stringified (the rename's contract).
        string key = sessionId.ToString();

        IReadOnlyList<ConversationDto> all = ConversationStore.List();
        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].SessionId, Is.EqualTo(key));
        Assert.That(all[0].AgentName, Is.EqualTo("claude"));
        Assert.That(all[0].DocTitle, Is.EqualTo("MyDoc.3dm"));

        Assert.That(ConversationStore.TryLoad(key, out ConversationDto loaded), Is.True);
        Assert.That(loaded.SessionId, Is.EqualTo(key));
        Assert.That(loaded.Lifecycle, Has.Count.EqualTo(1));
        Assert.That(loaded.Turns, Has.Count.EqualTo(1));

        TurnDto turn = loaded.Turns[0];
        Assert.That(turn.Prompt, Is.EqualTo("hello"));
        Assert.That(turn.CompletedAt, Is.Not.Null);
        Assert.That(turn.Events, Has.Count.EqualTo(2));
        Assert.That(turn.Events[1].Id, Is.EqualTo("toolu_1"));
        Assert.That(turn.Events[1].Result, Is.EqualTo("created"));
    }

    [Test]
    public void Persisted_transcript_keyed_by_legacy_sessionId_field_still_loads()
    {
        // A transcript written by the pre-rename build: the serialized field is 'sessionId' (camelCase
        // via McpSerializer). The rename must not have moved this field, so it must still deserialize.
        string key = Guid.NewGuid().ToString();
        string legacyJson =
            $$"""
            {"sessionId":"{{key}}","agentName":"codex","docTitle":"Old.3dm","startedAt":"2026-01-01T00:00:00+00:00","lifecycle":[],"turns":[{"prompt":"q","startedAt":"2026-01-01T00:00:00+00:00","completedAt":"2026-01-01T00:00:01+00:00","events":[]}]}
            """;
        AISettings.Conversations.SetString(key, legacyJson);

        Assert.That(ConversationStore.TryLoad(key, out ConversationDto loaded), Is.True);
        Assert.That(loaded.SessionId, Is.EqualTo(key));
        Assert.That(loaded.AgentName, Is.EqualTo("codex"));
        Assert.That(loaded.DocTitle, Is.EqualTo("Old.3dm"));
        Assert.That(loaded.Turns, Has.Count.EqualTo(1));
        Assert.That(loaded.Turns[0].Prompt, Is.EqualTo("q"));
    }
}
