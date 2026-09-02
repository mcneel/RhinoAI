using RhinoAI.Integration.Tests.Harness;

namespace RhinoAI.Integration.Tests;

// Pins down slot-routing semantics for plugin-side tool calls:
//   - An explicit `slot` arg must route to that exact Rhino.
//   - An unknown `slot` arg must produce a structured slot_not_found payload
//     (not a hang, and not a generic MCP error).
[TestFixture]
public sealed class ToolDispatchBySlotTests : RouterFixture
{
    [Test]
    public async Task explicit_slot_routes_to_correct_rhino()
    {
        ReturnResult spawnA = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        ReturnResult spawnB = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        string slotA = spawnA.Payload!.Value.GetProperty("slotId").GetString()!;
        string slotB = spawnB.Payload!.Value.GetProperty("slotId").GetString()!;

        // Drop three lines into slot A; leave slot B untouched.
        _ = await _router.CallToolAsync("run_python", Args.Of(
            ("slot", (object?)slotA),
            ("script", """
                from Rhino.Geometry import Point3d, Line
                doc = __rhino_doc__
                for i in range(3):
                    doc.Objects.AddLine(Line(Point3d(i, 0, 0), Point3d(i, 1, 0)))
                """)));

        ReturnResult listA = await _router.CallToolAsync("list_objects", Args.Of(("slot", slotA)));
        ReturnResult listB = await _router.CallToolAsync("list_objects", Args.Of(("slot", slotB)));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(listA.Payload?.GetProperty("count").GetInt32(), Is.EqualTo(3));
            Assert.That(listB.Payload?.GetProperty("count").GetInt32(), Is.EqualTo(0));
        }));
    }

    [Test]
    public async Task tool_call_with_unknown_slot_returns_slot_not_found_payload()
    {
        // No spawn — just call a plugin tool with a bogus slot id. The router
        // must short-circuit in the dispatcher with a structured error, not
        // attempt to auto-spawn and not hang.
        ReturnResult response = await _router.CallToolAsync(
            "list_objects",
            Args.Of(("slot", "made-up-slot-xyz")));

        Assert.That(response.Error?.Code, Is.EqualTo("slot_not_found"));
        Assert.That(response.Error?.Message, Does.Contain("made-up-slot-xyz"));
    }
}
