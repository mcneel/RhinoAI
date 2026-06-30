using RhMcp;

namespace RhMcp.StreamJson.Tests;

// The session-total arithmetic on TokenUsage: tokens always sum; cost coalesces so a tokens-only
// session stays null while any reported cost carries through and like-for-like costs add.
[TestFixture]
public sealed class TokenUsageTests
{
    [Test]
    public void Empty_is_empty_and_zero()
    {
        Assert.That(TokenUsage.Empty.IsEmpty, Is.True);
        Assert.That(TokenUsage.Empty.TotalTokens, Is.EqualTo(0));
        Assert.That(TokenUsage.Empty.CostUsd, Is.Null);
    }

    [Test]
    public void Sum_adds_tokens_and_keeps_cost_null_when_neither_reports()
    {
        TokenUsage total = new TokenUsage(10, 5) + new TokenUsage(20, 7);
        Assert.That(total.InputTokens, Is.EqualTo(30));
        Assert.That(total.OutputTokens, Is.EqualTo(12));
        Assert.That(total.CostUsd, Is.Null);
    }

    [Test]
    public void Sum_carries_a_single_reported_cost_and_adds_two()
    {
        Assert.That((new TokenUsage(1, 1, 0.5m) + new TokenUsage(1, 1)).CostUsd, Is.EqualTo(0.5m));
        Assert.That((new TokenUsage(1, 1) + new TokenUsage(1, 1, 0.5m)).CostUsd, Is.EqualTo(0.5m));
        Assert.That((new TokenUsage(1, 1, 0.5m) + new TokenUsage(1, 1, 0.25m)).CostUsd, Is.EqualTo(0.75m));
    }

    [Test]
    public void Cost_only_usage_is_not_empty()
    {
        Assert.That(new TokenUsage(0, 0, 0.01m).IsEmpty, Is.False);
    }
}
