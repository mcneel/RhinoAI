using System.Diagnostics;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Slot rows in the SQLite registry are intent, not liveness — RhinoManager
// probes pid + port on every list_slots call and prunes dead rows. These tests
// pin down the reaping behaviour by killing the Rhino out from under the router
// and asserting that list_slots no longer reports the slot.
[TestFixture]
public sealed class SlotReapingTests : RouterFixture
{
    [Test]
    public async Task externally_killed_rhino_is_pruned_from_list_slots()
    {
        ReturnResult spawn = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        int pid = spawn.Payload!.Value.GetProperty("pid").GetInt32();

        KillExternally(pid);

        // Give the OS a moment to actually tear the process down before we
        // probe pid+port from inside the router.
        for (int i = 0; i < 50 && IsProcessAlive(pid); i++)
        {
            await Task.Delay(100);
        }
        Assert.That(IsProcessAlive(pid), Is.False, "Test could not kill the spawned Rhino; reaping cannot be verified.");

        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public async Task animal_name_freed_by_reap_is_reusable_on_next_spawn()
    {
        // Two consecutive spawns: kill the first, spawn again, and verify the
        // second spawn either reuses the freed name or progresses without
        // waiting for the stale row. We don't pin the exact name (the pool's
        // ordering is implementation detail) — only that we can re-spawn and
        // list_slots returns one healthy row.
        ReturnResult spawn = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        int pid = spawn.Payload!.Value.GetProperty("pid").GetInt32();
        string firstSlotId = spawn.Payload.Value.GetProperty("slotId").GetString()!;

        KillExternally(pid);
        for (int i = 0; i < 50 && IsProcessAlive(pid); i++)
        {
            await Task.Delay(100);
        }
        Assert.That(IsProcessAlive(pid), Is.False, "Test could not kill the first Rhino; reap-then-respawn cannot be verified.");

        ReturnResult respawn = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        Assert.That(respawn.Error, Is.Null,
            $"Respawn after reap should succeed. Error: {respawn.Error?.Code}: {respawn.Error?.Message}");

        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(1));
        // Pool ordering is implementation detail — reusing the freed name or
        // picking the next one in the pool are both acceptable. We deliberately
        // don't pin `firstSlotId` here; the count assertion above is the contract.
        _ = firstSlotId;
    }

    private static void KillExternally(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* already gone */ }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
