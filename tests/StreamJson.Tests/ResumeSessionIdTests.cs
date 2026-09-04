using System.Diagnostics;

namespace RhinoAI.StreamJson.Tests;

// Resume hangs entirely off one identifier: the id the CLI opens its session under has to be the id
// the transcript is saved under, or --resume names a session that never existed. It did not, which
// is why resuming reported "No conversation found with session ID".
[TestFixture]
public sealed class ResumeSessionIdTests
{
    private static AgentDefinition ClaudeDef() =>
        new("claude", AgentAdapter.Claude, "claude", [], string.Empty, [], string.Empty, Enabled: true, IsBuiltin: true);

    private static List<string> Arguments(string sessionId, bool resume)
    {
        ProcessStartInfo psi = new();
        new ClaudeStreamJsonParser(ClaudeDef())
            .ConfigureArguments(psi, "http://localhost:1234/agent", sessionId, [], resume);
        return [.. psi.ArgumentList];
    }

    [Test]
    public void A_fresh_session_declares_its_id_and_a_later_spawn_resumes_it()
    {
        const string id = "e6c56a9f-a857-473e-9039-3cc71bcafc08";

        List<string> fresh = Arguments(id, resume: false);
        Assert.That(fresh, Does.Contain("--session-id"));
        Assert.That(fresh[fresh.IndexOf("--session-id") + 1], Is.EqualTo(id));
        Assert.That(fresh, Does.Not.Contain("--resume"));

        List<string> again = Arguments(id, resume: true);
        Assert.That(again, Does.Contain("--resume"));
        Assert.That(again[again.IndexOf("--resume") + 1], Is.EqualTo(id),
            "the resume token has to be the id the session was opened under");
        Assert.That(again, Does.Not.Contain("--session-id"));
    }

    [SetUp]
    public void Reset() => AISettings.ResetForTest();

    [Test]
    public void A_restored_conversation_keeps_the_id_it_was_saved_under()
    {
        Conversation original = new(Guid.NewGuid(), "claude", "tower.3dm");
        original.BeginTurn("hello");
        original.CompleteTurn();

        ConversationStore.Save(original);
        Assert.That(ConversationStore.TryLoad(original.AgentSessionId.ToString(), out ConversationDto dto), Is.True,
            "the transcript is keyed by the session id the CLI opened");

        Conversation restored = Conversation.Restore(dto);
        Assert.That(restored.AgentSessionId, Is.EqualTo(original.AgentSessionId),
            "AgentFactory.CreateResumed passes this straight to --resume");
    }

    [Test]
    public void A_rotated_id_leaves_exactly_one_row_in_the_history()
    {
        // The whole sequence a rejected resume produces: saved under A, the CLI refuses to resume A,
        // a fresh session opens under B, and the transcript continues. Two rows for one conversation
        // would show up in the history as a duplicate whose Resume can only fail again.
        Conversation convo = new(Guid.NewGuid(), "claude", "tower.3dm");
        convo.BeginTurn("hello");
        convo.CompleteTurn();

        Guid dead = convo.AgentSessionId;
        ConversationStore.Save(convo);
        Assert.That(ConversationStore.TryLoad(dead.ToString(), out _), Is.True);

        Guid fresh = Guid.NewGuid();
        convo.AdoptSessionId(fresh);
        ConversationStore.Save(convo);
        ConversationStore.Delete(dead.ToString());

        Assert.That(ConversationStore.TryLoad(fresh.ToString(), out _), Is.True);
        Assert.That(ConversationStore.TryLoad(dead.ToString(), out _), Is.False);
        Assert.That(ConversationStore.List().Count(c => c.DocTitle == "tower.3dm"), Is.EqualTo(1));
    }

    [Test]
    public void Deleting_a_transcript_that_is_not_there_is_harmless()
    {
        Assert.That(() => ConversationStore.Delete(Guid.NewGuid().ToString()), Throws.Nothing);
        Assert.That(() => ConversationStore.Delete(string.Empty), Throws.Nothing);
        Assert.That(() => ConversationStore.Delete("not-a-guid"), Throws.Nothing);
    }

    [Test]
    public void Adopting_a_new_id_is_what_gets_persisted()
    {
        // A rejected resume forces the agent to open a fresh session under a new id. The saved
        // transcript has to follow, or the next resume fails the same way.
        Conversation convo = new(Guid.NewGuid(), "claude", "tower.3dm");
        convo.BeginTurn("hello");
        convo.CompleteTurn();

        Guid rotated = Guid.NewGuid();
        convo.AdoptSessionId(rotated);
        ConversationStore.Save(convo);

        Assert.That(convo.AgentSessionId, Is.EqualTo(rotated));
        Assert.That(ConversationStore.TryLoad(rotated.ToString(), out ConversationDto saved), Is.True,
            "the transcript has to be findable under the id the CLI will actually resume");
        Assert.That(saved.SessionId, Is.EqualTo(rotated.ToString()));
    }
}
