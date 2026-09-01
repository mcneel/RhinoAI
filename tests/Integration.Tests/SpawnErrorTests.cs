using RhinoAI.Integration.Tests.Harness;

namespace RhinoAI.Integration.Tests;

// Exercises the error branches of spawn_slot. These don't need a real Rhino —
// the version-resolution step in RhinoLocator throws FileNotFoundException for
// any string the locator doesn't recognise, which the tool maps to a structured
// `rhino_not_installed` payload before any process is launched.
[TestFixture]
internal sealed class SpawnErrorTests : SharedRouterFixture
{

    [Test]
    public async Task spawn_with_unknown_version_returns_rhino_not_installed()
    {
        ReturnResult result = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "garbage")));

        Assert.That(result.Error?.Code, Is.EqualTo("rhino_not_installed"));
        Assert.That(result.Error?.Message, Does.Contain("garbage"));

        // The launching placeholder must be cleaned up so the slot id is free
        // again — otherwise the next spawn waits 90s for the stale row.
        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(0));
    }

    // Regression: a version sent as a JSON number must coerce to string and reach
    // the locator, not fail string binding with a bare invocation error.
    [Test]
    public async Task spawn_with_unknown_numeric_version_returns_rhino_not_installed()
    {
        // 42 is passed as a JSON number, not the string "42".
        ReturnResult result = await _router.CallToolAsync("spawn_slot", Args.Of(("version", 42)));

        Assert.That(result.Error?.Code, Is.EqualTo("rhino_not_installed"));
        Assert.That(result.Error?.Message, Does.Contain("42"));
    }

    [Test]
    public async Task failed_spawn_does_not_leave_launching_row_behind()
    {
        _ = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "garbage")));
        _ = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "garbage")));

        // Two failed spawns in a row must still not produce ready slots and
        // must not pile up placeholders. list_slots filters to Ready rows, so
        // a non-empty result here would mean adoption-as-ready (wrong) or
        // duplicate launching rows leaked through (also wrong).
        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(0));
    }
}
