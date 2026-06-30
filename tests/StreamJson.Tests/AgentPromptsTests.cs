using RhMcp;

namespace RhMcp.StreamJson.Tests;

// Pins the pull-only grounding contract (PLAN locked decision: no auto-injection, mitigated by an
// always-on steer that is "never dropped"). Compose is the one place that invariant lives in code,
// so these tests guard that a future edit can't silently drop a steer or let a custom system prompt
// replace (rather than follow) them, for both the empty- and non-empty-prompt branches.
[TestFixture]
public sealed class AgentPromptsTests
{
    [Test]
    public void Compose_with_empty_prompt_carries_all_three_always_on_steers()
    {
        string composed = AgentPrompts.Compose(string.Empty);

        Assert.That(composed, Does.Contain(AgentPrompts.AskUserSteer));
        Assert.That(composed, Does.Contain(AgentPrompts.GroundingSteer));
        Assert.That(composed, Does.Contain(AgentPrompts.GrasshopperSteer));
    }

    [Test]
    public void Compose_with_a_custom_prompt_keeps_every_steer()
    {
        string composed = AgentPrompts.Compose("custom");

        Assert.That(composed, Does.Contain(AgentPrompts.AskUserSteer));
        Assert.That(composed, Does.Contain(AgentPrompts.GroundingSteer));
        Assert.That(composed, Does.Contain(AgentPrompts.GrasshopperSteer));
    }

    [Test]
    public void Compose_appends_the_system_prompt_after_the_steers_never_replacing_them()
    {
        const string systemPrompt = "agent-specific instructions";
        string composed = AgentPrompts.Compose(systemPrompt);

        Assert.That(composed, Does.Contain(systemPrompt));
        // The prompt follows the steers, never replaces them: every steer index precedes it.
        Assert.That(composed.IndexOf(systemPrompt, StringComparison.Ordinal),
            Is.GreaterThan(composed.IndexOf(AgentPrompts.AskUserSteer, StringComparison.Ordinal)));
        Assert.That(composed.IndexOf(systemPrompt, StringComparison.Ordinal),
            Is.GreaterThan(composed.IndexOf(AgentPrompts.GroundingSteer, StringComparison.Ordinal)));
        Assert.That(composed.IndexOf(systemPrompt, StringComparison.Ordinal),
            Is.GreaterThan(composed.IndexOf(AgentPrompts.GrasshopperSteer, StringComparison.Ordinal)));
    }

    [Test]
    public void Compose_with_empty_prompt_is_the_steers_alone_with_no_trailing_prompt()
    {
        // The empty branch must be exactly the three steers (no stray appended segment), so a future
        // edit that always appends can't smuggle an empty trailing block.
        string steersOnly = AgentPrompts.AskUserSteer + "\n\n" + AgentPrompts.GroundingSteer + "\n\n" + AgentPrompts.GrasshopperSteer;
        Assert.That(AgentPrompts.Compose(string.Empty), Is.EqualTo(steersOnly));
    }
}
