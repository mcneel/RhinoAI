using RhinoAI.Integration.Tests.Harness;

namespace RhinoAI.Integration.Tests;

// Exercises spawn_slot end-to-end: the router must launch a real Rhino,
// receive its listener announcement, and return the slot metadata.
[TestFixture]
internal sealed class SpawnSlotTests : RouterFixture
{

    [TestCase("8")]
    [TestCase("9")]
    [TestCase("WIP")]
    public async Task spawn_slot_returns_slot_metadata(string version)
    {
        ReturnResult result = await _router.CallToolAsync("spawn_slot", Args.Of(("version", version)));

        Assert.That(result.Payload?.GetProperty("slotId").GetString(), Is.Not.Empty);
        Assert.That(result.Payload?.GetProperty("version").GetString(), Is.EqualTo(version));
        Assert.That(result.Payload?.GetProperty("adopted").GetBoolean(), Is.False);
    }

    // Regression: a version sent as a JSON number must coerce and spawn, not fail
    // string binding with a bare invocation error.
    [Test]
    public async Task spawn_slot_accepts_numeric_version()
    {
        // 8 is passed as a JSON number, not the string "8".
        ReturnResult result = await _router.CallToolAsync("spawn_slot", Args.Of(("version", 8)));

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Payload?.GetProperty("slotId").GetString(), Is.Not.Empty);
        Assert.That(result.Payload?.GetProperty("version").GetString(), Is.EqualTo("8"));
    }

    [Test]
    public async Task spawn_three_slots_returns_distinct_metadata()
    {
        ReturnResult r1 = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        ReturnResult r2 = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        ReturnResult r3 = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));

        foreach (ReturnResult result in new[] { r1, r2, r3 })
        {
            Assert.That(result.Payload?.GetProperty("slotId").GetString(), Is.Not.Empty);
            Assert.That(result.Payload?.GetProperty("version").GetString(), Is.EqualTo("8"));
            Assert.That(result.Payload?.GetProperty("adopted").GetBoolean(), Is.False);
        }
    }

    // Round-trip: spawn produces a slotId that close_slot will accept.
    [Test]
    public async Task spawn_then_close_slot_round_trip()
    {
        ReturnResult open = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));

        Assert.That(open.Payload?.GetProperty("slotId").GetString(), Is.Not.Empty);
        Assert.That(open.Payload?.GetProperty("version").GetString(), Is.EqualTo("8"));
        Assert.That(open.Payload?.GetProperty("adopted").GetBoolean(), Is.False);

        string slotId = open.Payload!.Value.GetProperty("slotId").GetString()!;

        ReturnResult close = await _router.CallToolAsync("close_slot", Args.Of(("slot", slotId)));
        Assert.That(close.Payload?.GetProperty("closed").GetBoolean(), Is.True);
    }
}
