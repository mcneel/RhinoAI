using RhMcp;

namespace RhMcp.StreamJson.Tests;

// The overlay/dedup invariant AISettings.GetAgents relies on (PLAN W1: custom entries that alias a
// built-in name override the built-in in place, never duplicated; the built-ins are always present).
// Production now routes through this pure AgentRegistry.Overlay, and the test shim's GetAgents reuses
// the SAME method, so this fixture pins the contract once for both paths. Exercised directly against
// Overlay (no plugin), then through Refresh/Chain to prove the shim doesn't diverge from production.
[TestFixture]
public sealed class AgentRegistryOverlayTests
{
    private static AgentDefinition Custom(string name, AgentAdapter adapter, string command, bool enabled = true) =>
        new(name, adapter, command, [command], string.Empty, [], string.Empty, enabled, IsBuiltin: false);

    [Test]
    public void Overlay_with_no_custom_entries_is_the_builtins_unchanged()
    {
        IReadOnlyList<AgentDefinition> builtins = AgentRegistry.Builtins();

        IReadOnlyList<AgentDefinition> chain = AgentRegistry.Overlay(builtins, []);

        Assert.That(chain.Select(static a => a.Name), Is.EqualTo(builtins.Select(static a => a.Name)));
        Assert.That(chain, Has.All.Matches<AgentDefinition>(static a => a.IsBuiltin));
    }

    [Test]
    public void Overlay_replaces_an_aliasing_custom_entry_in_place_never_duplicating()
    {
        IReadOnlyList<AgentDefinition> builtins = AgentRegistry.Builtins();
        AgentDefinition customClaude = Custom("claude", AgentAdapter.Claude, "/opt/my/claude");

        IReadOnlyList<AgentDefinition> chain = AgentRegistry.Overlay(builtins, [customClaude]);

        // Same count as built-ins: the alias overrode, it did not append a second "claude".
        Assert.That(chain, Has.Count.EqualTo(builtins.Count));
        Assert.That(chain.Count(static a => a.Name == "claude"), Is.EqualTo(1));

        AgentDefinition claude = chain.Single(static a => a.Name == "claude");
        Assert.That(claude.Command, Is.EqualTo("/opt/my/claude")); // the custom command won
        Assert.That(claude.IsBuiltin, Is.True);                    // but the built-in flag is preserved
    }

    [Test]
    public void Overlay_keeps_the_builtin_slot_position_when_overriding()
    {
        IReadOnlyList<AgentDefinition> builtins = AgentRegistry.Builtins();
        AgentDefinition customCodex = Custom("codex", AgentAdapter.Codex, "/opt/my/codex");

        IReadOnlyList<AgentDefinition> chain = AgentRegistry.Overlay(builtins, [customCodex]);

        // Order is untouched: claude, codex, gemini. The override lands in codex's existing slot.
        Assert.That(chain.Select(static a => a.Name), Is.EqualTo(new[] { "claude", "codex", "gemini" }));
        Assert.That(chain[1].Command, Is.EqualTo("/opt/my/codex"));
    }

    [Test]
    public void Overlay_appends_a_new_named_custom_entry_after_the_builtins()
    {
        IReadOnlyList<AgentDefinition> builtins = AgentRegistry.Builtins();
        AgentDefinition mine = Custom("my-agent", AgentAdapter.Claude, "/opt/my/agent");

        IReadOnlyList<AgentDefinition> chain = AgentRegistry.Overlay(builtins, [mine]);

        Assert.That(chain, Has.Count.EqualTo(builtins.Count + 1));
        Assert.That(chain[^1].Name, Is.EqualTo("my-agent"));
        Assert.That(chain[^1].IsBuiltin, Is.False); // a genuinely new entry stays non-built-in
    }

    [Test]
    public void GetAgents_shim_flows_custom_entries_through_the_same_overlay()
    {
        AISettings.ResetForTest();
        try
        {
            AISettings.SetCustomAgentsForTest([Custom("claude", AgentAdapter.Claude, "/opt/my/claude")]);

            IReadOnlyList<AgentDefinition> agents = AISettings.GetAgents();
            Assert.That(agents.Count(static a => a.Name == "claude"), Is.EqualTo(1)); // de-duped
            Assert.That(agents.Single(static a => a.Name == "claude").Command, Is.EqualTo("/opt/my/claude"));
            Assert.That(agents.Single(static a => a.Name == "claude").IsBuiltin, Is.True);
        }
        finally
        {
            AISettings.ResetForTest();
        }
    }
}
