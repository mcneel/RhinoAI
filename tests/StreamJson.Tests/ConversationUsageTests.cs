using RhMcp;

namespace RhMcp.StreamJson.Tests;

// RecordUsage lands on the current turn and rolls up into SessionUsage; empty usage and a record
// arriving after the turn already closed are both ignored rather than mis-filed.
[TestFixture]
public sealed class ConversationUsageTests
{
    private static Conversation NewConversation() => new(Guid.NewGuid(), "claude", "Untitled");

    [Test]
    public void Recorded_usage_lands_on_the_current_turn_and_sums_into_the_session()
    {
        Conversation convo = NewConversation();

        convo.BeginTurn("one");
        convo.RecordUsage(new TokenUsage(100, 40, 0.01m));
        convo.CompleteTurn();

        convo.BeginTurn("two");
        convo.RecordUsage(new TokenUsage(50, 20));
        convo.CompleteTurn();

        Assert.That(convo.Turns[0].Usage.TotalTokens, Is.EqualTo(140));
        Assert.That(convo.Turns[1].Usage.TotalTokens, Is.EqualTo(70));

        Assert.That(convo.SessionUsage.InputTokens, Is.EqualTo(150));
        Assert.That(convo.SessionUsage.OutputTokens, Is.EqualTo(60));
        Assert.That(convo.SessionUsage.CostUsd, Is.EqualTo(0.01m)); // one turn reported a cost
    }

    [Test]
    public void Empty_usage_is_not_recorded()
    {
        Conversation convo = NewConversation();
        convo.BeginTurn("one");
        convo.RecordUsage(TokenUsage.Empty);

        Assert.That(convo.Turns[0].Usage.IsEmpty, Is.True);
        Assert.That(convo.SessionUsage.IsEmpty, Is.True);
    }

    [Test]
    public void Usage_after_turn_closed_is_dropped_not_misfiled()
    {
        Conversation convo = NewConversation();
        convo.BeginTurn("one");
        convo.CompleteTurn(); // Current cleared

        convo.RecordUsage(new TokenUsage(999, 999)); // no current turn to attach to

        Assert.That(convo.Turns[0].Usage.IsEmpty, Is.True);
        Assert.That(convo.SessionUsage.IsEmpty, Is.True);
    }
}
