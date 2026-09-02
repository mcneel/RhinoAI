using System.Text.Json;
using RhinoAI.Integration.Tests.Harness;

namespace RhinoAI.Integration.Tests;

// Pins down the JSON shape list_slots returns post-spawn / post-close. The
// existing ListSlotsTests fixture only asserts the empty case; this one
// asserts the populated case and the close-removes path.
[TestFixture]
public sealed class ListSlotsShapeTests : RouterFixture
{
    [Test]
    public async Task list_slots_after_spawn_contains_expected_fields()
    {
        ReturnResult spawn = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        string slotId = spawn.Payload!.Value.GetProperty("slotId").GetString()!;
        int port = spawn.Payload.Value.GetProperty("port").GetInt32();

        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(1));

        JsonElement slot = list.Payload!.Value[0];
        Assert.That(slot.GetProperty("slotId").GetString(), Is.EqualTo(slotId));
        Assert.That(slot.GetProperty("port").GetInt32(), Is.EqualTo(port));
        Assert.That(slot.GetProperty("version").GetString(), Is.EqualTo("8"));
        Assert.That(slot.GetProperty("adopted").GetBoolean(), Is.False);
        Assert.That(slot.GetProperty("pid").GetInt32(), Is.GreaterThan(0));
        Assert.That(slot.GetProperty("endpoint").GetString(), Is.EqualTo($"http://localhost:{port}"));
    }

    [Test]
    public async Task list_slots_after_close_does_not_include_closed_slot()
    {
        ReturnResult spawn = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        string slotId = spawn.Payload!.Value.GetProperty("slotId").GetString()!;

        ReturnResult close = await _router.CallToolAsync("close_slot", Args.Of(("slot", slotId)));
        Assert.That(close.Payload?.GetProperty("closed").GetBoolean(), Is.True);

        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public async Task close_slot_twice_returns_slot_not_found_on_second_call()
    {
        ReturnResult spawn = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        string slotId = spawn.Payload!.Value.GetProperty("slotId").GetString()!;

        _ = await _router.CallToolAsync("close_slot", Args.Of(("slot", slotId)));

        ReturnResult secondClose = await _router.CallToolAsync("close_slot", Args.Of(("slot", slotId)));
        Assert.That(secondClose.Error?.Code, Is.EqualTo("slot_not_found"));
    }
}
