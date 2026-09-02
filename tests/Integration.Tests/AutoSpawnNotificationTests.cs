using RhinoAI.Integration.Tests.Harness;

namespace RhinoAI.Integration.Tests;

// Pins the autoSpawnedSlot side-channel contract: when a tool is called without
// a `slot` argument and the router has to launch a Rhino to serve it, the
// response must carry an `autoSpawnedSlot` payload telling the agent what just
// happened. Subsequent slotless calls reuse the same Rhino and must NOT
// re-announce — the agent learns the slot id once.
//
// Uses RouterFixture (fresh router per test) so each test starts with no slots.
[TestFixture]
public sealed class AutoSpawnNotificationTests : RouterFixture
{
    [Test]
    public async Task slotless_tool_call_on_fresh_router_announces_auto_spawn()
    {
        ReturnResult result = await _router.CallToolAsync("run_python", Args.Of(("script", "pass")));

        Assert.That(result.Error, Is.Null,
            $"run_python should succeed. Error: {result.Error?.Code}: {result.Error?.Message}");
        Assert.That(result.AutoSpawnedSlot, Is.Not.Null,
            "First slotless call must populate autoSpawnedSlot so the agent learns the slot id.");
        Assert.That(result.AutoSpawnedSlot!.SlotId, Is.Not.Empty);
        Assert.That(result.AutoSpawnedSlot.Version, Is.EqualTo("8"));
        Assert.That(result.AutoSpawnedSlot.Reason, Does.Contain("run_python"));
    }

    [Test]
    public async Task subsequent_slotless_call_reuses_slot_and_omits_auto_spawn()
    {
        ReturnResult first = await _router.CallToolAsync("run_python", Args.Of(("script", "pass")));
        Assert.That(first.AutoSpawnedSlot, Is.Not.Null, "Precondition: first call should auto-spawn.");
        string firstSlot = first.AutoSpawnedSlot!.SlotId;

        ReturnResult second = await _router.CallToolAsync("run_python", Args.Of(("script", "pass")));
        Assert.That(second.Error, Is.Null);
        Assert.That(second.AutoSpawnedSlot, Is.Null,
            "Reuse of an existing slot must not re-announce — the agent already knows about it.");

        // And list_slots confirms there's still only one Rhino.
        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(1));
        Assert.That(list.Payload!.Value[0].GetProperty("slotId").GetString(), Is.EqualTo(firstSlot));
    }

    [Test]
    public async Task explicit_slot_argument_does_not_trigger_auto_spawn_notification()
    {
        // Spawn first via the explicit tool — agent knows the slot id from the
        // spawn_slot response, no side-channel needed.
        ReturnResult spawn = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        string slot = spawn.Payload!.Value.GetProperty("slotId").GetString()!;

        ReturnResult result = await _router.CallToolAsync(
            "run_python",
            Args.Of(("slot", (object?)slot), ("script", "pass")));

        Assert.That(result.Error, Is.Null);
        Assert.That(result.AutoSpawnedSlot, Is.Null,
            "Explicit `slot` arg means the agent already knows the slot — no announcement.");
    }
}
