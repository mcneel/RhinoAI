using System.Threading;
using RhMcp;

namespace RhMcp.StreamJson.Tests;

// ConversationStore beyond the round-trip: the 50-conversation cap prunes the oldest on Save, the
// list comes back newest-first, and a corrupt on-disk slot is skipped rather than thrown. Backed by
// the in-memory PersistentSettings shim, reset per test.
[TestFixture]
public sealed class ConversationStorePruneTests
{
    private const int Cap = 50;

    [SetUp]
    public void Reset() => AISettings.ResetForTest();

    private static void SaveTrivial(string agentName)
    {
        Conversation convo = new(Guid.NewGuid(), agentName, "Untitled");
        convo.BeginTurn("q");
        convo.CompleteTurn();
        ConversationStore.Save(convo);
    }

    [Test]
    public void Save_beyond_the_cap_prunes_down_to_the_cap()
    {
        for (int i = 0; i < Cap + 5; i++)
            SaveTrivial($"agent-{i}");

        Assert.That(ConversationStore.List(), Has.Count.EqualTo(Cap));
    }

    [Test]
    public void Pruning_drops_the_oldest_and_keeps_the_newest_first()
    {
        // Distinct StartedAt so the newest-first sort (and thus which slot is pruned) is deterministic.
        // The constructor stamps StartedAt = UtcNow, so a tiny spacing separates each conversation.
        Conversation oldest = new(Guid.NewGuid(), "oldest", "Untitled");
        oldest.BeginTurn("q");
        oldest.CompleteTurn();
        ConversationStore.Save(oldest);

        Thread.Sleep(5);

        for (int i = 0; i < Cap; i++)
        {
            Thread.Sleep(2);
            SaveTrivial($"newer-{i}");
        }

        IReadOnlyList<ConversationDto> all = ConversationStore.List();
        Assert.That(all, Has.Count.EqualTo(Cap));
        Assert.That(all.Any(c => c.AgentName == "oldest"), Is.False); // the oldest got pruned
        // List is newest-first: the head's StartedAt is the max.
        Assert.That(all[0].StartedAt, Is.GreaterThanOrEqualTo(all[^1].StartedAt));
    }

    [Test]
    public void A_corrupt_slot_is_skipped_not_thrown()
    {
        SaveTrivial("good");
        AISettings.Conversations.SetString("garbage-key", "{ this is not valid json ");

        IReadOnlyList<ConversationDto> all = ConversationStore.List();

        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].AgentName, Is.EqualTo("good"));
    }

    [Test]
    public void TryLoad_of_a_missing_key_returns_false()
    {
        Assert.That(ConversationStore.TryLoad(Guid.NewGuid().ToString(), out _), Is.False);
    }

    [Test]
    public void Re_saving_the_same_session_overwrites_in_place_not_duplicates()
    {
        Guid id = Guid.NewGuid();
        Conversation convo = new(id, "claude", "Untitled");
        convo.BeginTurn("first");
        convo.CompleteTurn();
        ConversationStore.Save(convo);

        convo.BeginTurn("second");
        convo.CompleteTurn();
        ConversationStore.Save(convo);

        IReadOnlyList<ConversationDto> all = ConversationStore.List();
        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].Turns, Has.Count.EqualTo(2));
    }
}
