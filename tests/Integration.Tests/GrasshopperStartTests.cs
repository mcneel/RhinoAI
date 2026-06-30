using System.Text.Json;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Exercises g1_start / g2_start: starting Grasshopper inside a spawned Rhino
// should produce its own slot entry alongside the parent Rhino slot.
[TestFixture]
public sealed class GrasshopperStartTests : RouterFixture
{

    [Test]
    public async Task g2_start_in_rhino_8_produces_distinct_slot()
    {
        _ = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));

        ReturnResult gh2 = await _router.CallToolAsync("g2_start");
        Assert.That(gh2.Payload?.GetString(), Does.Contain("Opened G2").IgnoreCase);

        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(2));

        List<JsonElement> slots = list.Payload!.Value.EnumerateArray().ToList();
        foreach (string property in new[] { "slotId", "port", "endpoint" })
        {
            HashSet<string> set = slots.Select(j => j.GetProperty(property).ToString().ToLowerInvariant()).ToHashSet();
            Assert.That(set, Has.Count.EqualTo(2));
        }
    }

    // g2_start with no Rhino already spawned should auto-spawn a host. Today
    // that path picks Rhino WIP — the assertion mirrors that contract.
    [Test]
    public async Task g2_start_with_no_host_spawns_one_slot()
    {
        ReturnResult gh2 = await _router.CallToolAsync("g2_start");
        Assert.That(gh2.Payload?.GetString(), Does.Contain("Opened G2").IgnoreCase);

        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(1));

        JsonElement slot = list.Payload!.Value.EnumerateArray().Single();
        Assert.That(slot.GetProperty("version").GetString(), Is.EqualTo("WIP"),
            "g2_start with no host auto-spawns Rhino WIP");
    }

    [TestCase("8")]
    [TestCase("WIP")]
    public async Task g1_start_inside_rhino_opens_grasshopper(string version)
    {
        _ = await _router.CallToolAsync("spawn_slot", Args.Of(("version", version)));

        ReturnResult gh1 = await _router.CallToolAsync("g1_start");
        Assert.That(gh1.Payload?.GetString(), Does.Contain("Opened Grasshopper").IgnoreCase);
    }
}
