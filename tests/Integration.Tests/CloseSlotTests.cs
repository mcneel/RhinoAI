using RhinoAI.Integration.Tests.Harness;

namespace RhinoAI.Integration.Tests;

// Exercises the router's close_slot tool directly. No Rhino install required —
// these tests run against a freshly-spawned router with an isolated state dir.
[TestFixture]
internal sealed class CloseSlotTests : SharedRouterFixture
{
    // Regression: a status-agnostic existence check is required so launching
    // slots are not mistaken for missing slots. The envelope's `error` field
    // is a structured ErrorInfo { code, message } that agents key off of when
    // deciding whether to retry, list slots, etc.
    [Test]
    public async Task close_slot_returns_slot_not_found_for_unknown_slot()
    {
        ReturnResult result = await _router.CallToolAsync("close_slot", Args.Of(("slot", "does-not-exist")));

        Assert.That(result.Error?.Code, Is.EqualTo("slot_not_found"));
        Assert.That(result.Error?.Message, Does.Contain("does-not-exist"));
        Assert.That(result.Error?.Message, Does.Contain("list_slots"));
        Assert.That(result.Payload, Is.Null);
    }

    // The advertised envelope promises null fields are omitted. The source-gen
    // policy is pinned by Router.Tests/ReturnResultTests; this end-to-end version
    // catches the orthogonal failure mode of something downstream (MCP SDK
    // transport, content-block wrap) re-introducing the null fields.
    [Test]
    public async Task close_slot_failure_envelope_omits_payload_and_autoSpawnedSlot_on_the_wire()
    {
        string json = await _router.CallToolTextAsync("close_slot", Args.Of(("slot", "another-bogus-slot")));

        Assert.That(json, Does.Not.Contain("\"payload\":null"));
        Assert.That(json, Does.Not.Contain("\"autoSpawnedSlot\":null"));
    }
}
