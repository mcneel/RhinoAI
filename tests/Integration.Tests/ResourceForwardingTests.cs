using ModelContextProtocol.Protocol;

using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// End-to-end coverage for the router's resource-forwarding layer:
//   - router-local built-ins (rhino://slots/primer) appear with no slots running
//   - plug-in primers (rhino://grasshopper/primer, rhino://scripting/primer)
//     appear after a slot is spawned
//   - resources/read returns the right content body
//   - reading a missing resource fails loudly
//
// The router-local list is satisfied entirely from the router binary's own
// mcp/ folder (copied alongside rhino-mcp-router during build). The plug-in
// list requires a live Rhino slot — those tests Inconclusive if spawn fails.
[TestFixture]
internal sealed class ResourceForwardingTests : RouterFixture
{
    [Test]
    public async Task list_with_no_slots_returns_only_router_builtins()
    {
        var resources = await _router.Client.ListResourcesAsync();

        Assert.That(resources.Select(r => r.Uri), Does.Contain("rhino://slots/primer"),
            "Router-local slots primer should be served even with no slots running.");
        // Plugin-only primers must not appear before any Rhino is up.
        Assert.That(resources.Select(r => r.Uri), Does.Not.Contain("rhino://grasshopper/primer"));
        Assert.That(resources.Select(r => r.Uri), Does.Not.Contain("rhino://scripting/primer"));
    }

    [Test]
    public async Task read_slots_primer_returns_router_local_markdown()
    {
        ReadResourceResult result = await _router.Client.ReadResourceAsync("rhino://slots/primer");

        Assert.That(result.Contents, Has.Count.GreaterThan(0));
        TextResourceContents text = (TextResourceContents)result.Contents[0];
        Assert.That(text.MimeType, Is.EqualTo("text/markdown"));
        Assert.That(text.Text, Does.Contain("Slots — primer"),
            "Router-local slots primer body should match the file shipped with the router.");
    }

    [Test]
    public async Task list_after_spawn_includes_plugin_primers()
    {
        ReturnResult spawn = await _router.CallToolAsync("spawn_slot");
        if (spawn.Error is not null)
            Assert.Inconclusive($"spawn_slot failed: {spawn.Error.Code} — {spawn.Error.Message}");

        var resources = await _router.Client.ListResourcesAsync();

        Assert.That(resources.Select(r => r.Uri), Does.Contain("rhino://grasshopper/primer"));
        Assert.That(resources.Select(r => r.Uri), Does.Contain("rhino://scripting/primer"));
        // Router-local still wins on the slots primer.
        Assert.That(resources.Select(r => r.Uri), Does.Contain("rhino://slots/primer"));
    }

    [Test]
    public async Task read_grasshopper_primer_returns_plugin_markdown()
    {
        ReturnResult spawn = await _router.CallToolAsync("spawn_slot");
        if (spawn.Error is not null)
            Assert.Inconclusive($"spawn_slot failed: {spawn.Error.Code} — {spawn.Error.Message}");

        // Populate the cache so the read path uses the fast direct-route, not
        // the broadcast fallback.
        _ = await _router.Client.ListResourcesAsync();

        ReadResourceResult result = await _router.Client.ReadResourceAsync("rhino://grasshopper/primer");

        Assert.That(result.Contents, Has.Count.GreaterThan(0));
        TextResourceContents text = (TextResourceContents)result.Contents[0];
        Assert.That(text.Text, Does.Contain("Grasshopper primer"));
    }

    [Test]
    public async Task read_unknown_resource_throws()
    {
        // Should fail rather than hang or silently return empty contents.
        bool threw = false;
        try
        {
            await _router.Client.ReadResourceAsync("rhino://does-not-exist");
        }
        catch
        {
            threw = true;
        }
        Assert.That(threw, Is.True, "Reading a non-existent resource should throw.");
    }
}
