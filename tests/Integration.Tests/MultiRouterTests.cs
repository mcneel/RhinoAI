using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Exercises the "many tools / many routers share the same slot" behaviour:
// repeated tool calls (from one router, or from several isolated routers)
// should not leave duplicate slot entries behind.
// Fresh router per test (RouterFixture, not SharedRouterFixture): the
// parameterized "share a single slot" cases assert list_slots count == 1, which
// is order-dependent if a sibling test (e.g. spawn_slot_across_two_versions)
// leaks its slots into a router shared across the whole fixture.
[TestFixture]
public sealed class MultiRouterTests : RouterFixture
{
    private RhinoMcpRouter? _router2;
    private RhinoMcpRouter? _router3;

    // The base fixture disposes _router per test; here we also need to clean up
    // the extra routers spawned by the cross-router test cases.
    [TearDown]
    public async Task DisposeExtraRouters()
    {
        foreach (RhinoMcpRouter? router in new[] { _router2, _router3 })
        {
            if (router is null) continue;
            try { await router.DisposeAsync(); } catch { /* best effort */ }
        }
        _router2 = null;
        _router3 = null;
    }

    [TestCase("8")]
    [TestCase("WIP")]
    public async Task repeated_tool_calls_from_one_router_share_a_single_slot(string version)
    {
        _ = await _router.CallToolTextAsync("list_objects", Args.Of(("version", version)));
        _ = await _router.CallToolTextAsync("list_objects", Args.Of(("version", version)));
        _ = await _router.CallToolTextAsync("list_objects", Args.Of(("version", version)));

        ReturnResult result = await _router.CallToolAsync("list_slots");
        Assert.That(result.Payload?.GetArrayLength(), Is.EqualTo(1));
    }

    // TODO : Each isolated router currently shares an adopted slot via the
    // announcement directory. Decide whether that's the intended contract;
    // if every router should get its own ID, this test will need to change.
    [TestCase("8", Ignore = "2nd call locks up?")]
    [TestCase("WIP", Ignore = "2nd call locks up?")]
    public async Task tool_calls_across_isolated_routers_share_a_single_slot(string version)
    {
        _router2 = await RhinoMcpRouter.LaunchIsolatedAsync();
        _router3 = await RhinoMcpRouter.LaunchIsolatedAsync();

        _ = await _router.CallToolTextAsync("list_objects", Args.Of(("version", version)));
        // TODO : 2nd call locks up — investigate before re-enabling.
        _ = await _router2.CallToolTextAsync("list_objects", Args.Of(("version", version)));
        _ = await _router3.CallToolTextAsync("list_objects", Args.Of(("version", version)));

        ReturnResult result = await _router.CallToolAsync("list_slots");
        Assert.That(result.Payload?.GetArrayLength(), Is.EqualTo(1));
    }

    // Spawning two slots for two different versions should produce two entries
    // in list_slots — the version is part of the slot identity, not collapsed.
    [Test]
    public async Task spawn_slot_across_two_versions_produces_two_slots()
    {
        _ = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        _ = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "WIP")));

        ReturnResult result = await _router.CallToolAsync("list_slots");
        Assert.That(result.Payload?.GetArrayLength(), Is.EqualTo(2));
    }
}
