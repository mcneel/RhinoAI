using System.Text.Json;
using RhinoAI.Integration.Tests.Harness;

namespace RhinoAI.Integration.Tests;

// Pins down the Windows-specific contract: every slot gets its own OS process,
// regardless of version. This is the inverse of MacSharedProcessTests and is
// the simpler invariant to verify — if either test ever fails, the shared-vs-
// isolated branch in RhinoManager has drifted. [Platform(Win)] excludes the
// case from macOS where the contract is the opposite.
[TestFixture]
[Platform("Win")]
public sealed class WindowsProcessIsolationTests : RouterFixture
{
    [Test]
    public async Task two_slots_same_version_have_distinct_pids_and_ports()
    {
        _ = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        _ = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));

        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(2));

        List<JsonElement> slots = list.Payload!.Value.EnumerateArray().ToList();
        HashSet<int> pids = slots.Select(s => s.GetProperty("pid").GetInt32()).ToHashSet();
        HashSet<int> ports = slots.Select(s => s.GetProperty("port").GetInt32()).ToHashSet();

        Assert.That(pids, Has.Count.EqualTo(2), "Windows slots must be backed by distinct Rhino processes.");
        Assert.That(ports, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task closing_one_slot_does_not_kill_the_other()
    {
        ReturnResult spawnA = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        ReturnResult spawnB = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        string slotA = spawnA.Payload!.Value.GetProperty("slotId").GetString()!;
        string slotB = spawnB.Payload!.Value.GetProperty("slotId").GetString()!;
        int pidB = spawnB.Payload.Value.GetProperty("pid").GetInt32();

        ReturnResult close = await _router.CallToolAsync("close_slot", Args.Of(("slot", slotA)));
        Assert.That(close.Payload?.GetProperty("closed").GetBoolean(), Is.True);

        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(1));

        JsonElement remaining = list.Payload!.Value[0];
        Assert.That(remaining.GetProperty("slotId").GetString(), Is.EqualTo(slotB));
        Assert.That(IsProcessAlive(pidB), Is.True, "Sibling slot's Rhino must outlive close_slot on a peer.");
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
