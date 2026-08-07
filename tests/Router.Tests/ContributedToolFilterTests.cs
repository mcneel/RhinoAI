using NUnit.Framework;
using RhMcp.Router;

namespace RhMcp.Router.Tests;

// Precedence rules applied to the tools a slot reports, before any of them are offered to a
// client. The input is the plugin registry's own list (_router_list_contributed_tools), so
// these rules are defense-in-depth against a peer process the router does not control rather
// than the primary filter: the underscore rule protects the router's control channel, and the
// compiled rule guards names the plugin cannot know — the generated proxies and the
// router-local slot tools.
[TestFixture]
internal sealed class ContributedToolFilterTests
{
    private static IReadOnlySet<string> Compiled(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    [Test]
    public void a_name_the_compiled_catalogue_does_not_have_is_contributable()
    {
        Assert.That(ContributedToolCatalog.IsContributable("gh_run_tool", Compiled("run_python")), Is.True);
    }

    // Mirrors the precedence the plugin's own dispatcher applies. Without it, any plug-in on the
    // machine could quietly replace a built-in by registering its name.
    [Test]
    public void a_compiled_name_is_never_shadowed()
    {
        Assert.That(ContributedToolCatalog.IsContributable("run_python", Compiled("run_python")), Is.False);
    }

    // MCP tool names are matched case-insensitively, so the shadowing check has to be too --
    // otherwise "Run_Python" walks straight past it.
    [Test]
    public void shadowing_is_checked_case_insensitively()
    {
        Assert.That(ContributedToolCatalog.IsContributable("Run_Python", Compiled("run_python")), Is.False);
    }

    // _router_spawn_listener, _router_close_listener, _router_quit_app and
    // _router_list_contributed_tools are how this router drives the plugin. A conforming plugin
    // cannot register such a name (ToolNamePattern requires a leading letter), but the reply
    // crosses a process boundary, so the router enforces it too.
    [TestCase("_router_spawn_listener")]
    [TestCase("_router_close_listener")]
    [TestCase("_router_quit_app")]
    [TestCase("_anything_at_all")]
    public void the_private_control_channel_is_withheld(string name)
    {
        Assert.That(ContributedToolCatalog.IsContributable(name, Compiled()), Is.False);
    }

    [TestCase("")]
    [TestCase(null)]
    public void a_nameless_tool_is_not_contributable(string? name)
    {
        Assert.That(ContributedToolCatalog.IsContributable(name!, Compiled()), Is.False);
    }

    // An underscore is legal inside a tool name; only a leading one is reserved.
    [Test]
    public void an_underscore_inside_a_name_is_fine()
    {
        Assert.That(ContributedToolCatalog.IsContributable("gh_list_tools", Compiled()), Is.True);
    }
}
