using System.IO;
using RhMcp;

namespace RhMcp.StreamJson.Tests;

// AgentRegistry resolution logic exercised without a loaded plugin: the chain comes from the
// AISettings shim, availability is a real File.Exists probe over each definition's SearchPaths (so
// we point them at real temp files, not a mock), and TryResolveActive picks the first Enabled &&
// Available with the configured default name preferred. Refresh() rewrites the static chain, so each
// test resets the shim and re-Refreshes; no [Parallelizable] because that state is shared.
[TestFixture]
public sealed class AgentRegistryResolutionTests
{
    private string ProbeDir { get; set; } = string.Empty;

    [SetUp]
    public void SetUp()
    {
        AISettings.ResetForTest();
        ProbeDir = Path.Combine(Path.GetTempPath(), "rhmcp-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ProbeDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(ProbeDir))
            Directory.Delete(ProbeDir, recursive: true);
        AISettings.ResetForTest();
    }

    // A real, existing path the File.Exists probe will accept -> the agent is Available.
    private string PresentPath(string name)
    {
        string path = Path.Combine(ProbeDir, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    // A path guaranteed not to exist -> the agent is Unavailable.
    private string MissingPath(string name) => Path.Combine(ProbeDir, "missing-" + name);

    private AgentDefinition Def(string name, AgentAdapter adapter, string searchPath, bool enabled) =>
        new(name, adapter, name, [searchPath], string.Empty, [], string.Empty, enabled, IsBuiltin: false);

    [Test]
    public void Refresh_marks_available_by_probing_search_paths()
    {
        AISettings.SetAgentsForTest(
        [
            Def("claude", AgentAdapter.Claude, PresentPath("claude"), enabled: true),
            Def("codex", AgentAdapter.Codex, MissingPath("codex"), enabled: true),
        ]);
        AgentRegistry.Refresh();

        ResolvedAgent claude = AgentRegistry.Chain.Single(r => r.Definition.Name == "claude");
        ResolvedAgent codex = AgentRegistry.Chain.Single(r => r.Definition.Name == "codex");
        Assert.That(claude.Available, Is.True);
        Assert.That(codex.Available, Is.False);
    }

    [Test]
    public void TryResolveActive_prefers_the_configured_default_when_available()
    {
        AISettings.DefaultAgentName = "codex";
        AISettings.SetAgentsForTest(
        [
            Def("claude", AgentAdapter.Claude, PresentPath("claude"), enabled: true),
            Def("codex", AgentAdapter.Codex, PresentPath("codex"), enabled: true),
        ]);
        AgentRegistry.Refresh();

        Assert.That(AgentRegistry.TryResolveActive(out AgentDefinition active), Is.True);
        Assert.That(active.Name, Is.EqualTo("codex"));
    }

    [Test]
    public void TryResolveActive_falls_to_chain_order_when_the_default_is_unavailable()
    {
        // Default prefers claude, but claude isn't installed; the first available in chain order wins.
        AISettings.DefaultAgentName = "claude";
        AISettings.SetAgentsForTest(
        [
            Def("claude", AgentAdapter.Claude, MissingPath("claude"), enabled: true),
            Def("codex", AgentAdapter.Codex, PresentPath("codex"), enabled: true),
            Def("gemini", AgentAdapter.Gemini, PresentPath("gemini"), enabled: true),
        ]);
        AgentRegistry.Refresh();

        Assert.That(AgentRegistry.TryResolveActive(out AgentDefinition active), Is.True);
        Assert.That(active.Name, Is.EqualTo("codex")); // first available, chain order
    }

    [Test]
    public void TryResolveActive_skips_disabled_even_when_available()
    {
        AISettings.DefaultAgentName = "claude";
        AISettings.SetAgentsForTest(
        [
            Def("claude", AgentAdapter.Claude, PresentPath("claude"), enabled: false), // installed but off
            Def("codex", AgentAdapter.Codex, PresentPath("codex"), enabled: true),
        ]);
        AgentRegistry.Refresh();

        Assert.That(AgentRegistry.TryResolveActive(out AgentDefinition active), Is.True);
        Assert.That(active.Name, Is.EqualTo("codex"));
    }

    [Test]
    public void TryResolveActive_is_false_when_nothing_is_enabled_and_available()
    {
        AISettings.SetAgentsForTest(
        [
            Def("claude", AgentAdapter.Claude, MissingPath("claude"), enabled: true),  // unavailable
            Def("codex", AgentAdapter.Codex, PresentPath("codex"), enabled: false),    // disabled
        ]);
        AgentRegistry.Refresh();

        Assert.That(AgentRegistry.TryResolveActive(out _), Is.False);
    }

    [Test]
    public void TryGet_finds_by_name_regardless_of_enabled_or_available()
    {
        AISettings.SetAgentsForTest(
        [
            Def("codex", AgentAdapter.Codex, MissingPath("codex"), enabled: false),
        ]);
        AgentRegistry.Refresh();

        Assert.That(AgentRegistry.TryGet("codex", out AgentDefinition def), Is.True);
        Assert.That(def.Adapter, Is.EqualTo(AgentAdapter.Codex));
        Assert.That(AgentRegistry.TryGet("nope", out _), Is.False);
    }

    [Test]
    public void Builtins_are_claude_first_then_codex_then_gemini()
    {
        IReadOnlyList<AgentDefinition> builtins = AgentRegistry.Builtins();
        Assert.That(builtins.Select(static b => b.Name), Is.EqualTo(new[] { "claude", "codex", "gemini" }));
        Assert.That(builtins, Has.All.Matches<AgentDefinition>(static b => b.IsBuiltin && b.Enabled));
    }

    [Test]
    public void Default_search_paths_carry_the_command_name_and_are_deduped()
    {
        IReadOnlyList<string> paths = AgentRegistry.DefaultSearchPaths("claude");
        Assert.That(paths, Is.Not.Empty);
        Assert.That(paths.Distinct().Count(), Is.EqualTo(paths.Count));
        Assert.That(paths, Has.Some.Matches<string>(static p => Path.GetFileName(p).StartsWith("claude")));
    }
}
