using System.Text.Json.Nodes;
using NUnit.Framework;
using RhMcp.Router;

namespace RhMcp.Router.Tests;

// Pins the wire shape of the ReturnResult envelope so the agent contract can't
// regress silently. The serializer is RouterJsonContext (AOT source-gen) — the
// shape these tests assert is exactly what hits the MCP transport, no separate
// JsonSerializerOptions layer in between.
[TestFixture]
public class ReturnResultTests
{
    [Test]
    public void Success_with_only_payload_omits_error_and_autoSpawnedSlot_fields()
    {
        // The JsonIgnoreCondition.WhenWritingNull policy declared on
        // RouterJsonContext is what keeps the envelope tidy. If someone changes
        // the policy or forgets to apply it to ReturnResult, the failure path
        // would still work but every success result would carry "error":null
        // and "autoSpawnedSlot":null — noise the agent has to ignore.
        ReturnResult result = new(
            Payload: JsonValue.Create("ok"),
            Error: null,
            AutoSpawnedSlot: null);

        string json = result.AsJson;

        Assert.That(json, Does.Contain("\"payload\""));
        Assert.That(json, Does.Not.Contain("\"error\""));
        Assert.That(json, Does.Not.Contain("\"autoSpawnedSlot\""));
        // Belt and braces: the literal "null" must not appear in the envelope
        // for the absent fields either, regardless of property-name policy.
        Assert.That(json, Does.Not.Contain(":null"));
    }

    [Test]
    public void Failure_with_only_error_omits_payload_and_autoSpawnedSlot_fields()
    {
        ReturnResult result = new(
            Payload: null,
            Error: new ErrorInfo(Code: "slot_not_found", Message: "..."),
            AutoSpawnedSlot: null);

        string json = result.AsJson;

        Assert.That(json, Does.Contain("\"error\""));
        Assert.That(json, Does.Not.Contain("\"payload\""));
        Assert.That(json, Does.Not.Contain("\"autoSpawnedSlot\""));
    }

    [Test]
    public void ErrorInfo_with_null_crashReportPath_omits_that_field()
    {
        // CrashReportPath is the optional sub-field on ErrorInfo. Same policy
        // must reach the nested record; otherwise non-crash errors carry a
        // misleading "crashReportPath":null.
        ReturnResult result = new(
            Payload: null,
            Error: new ErrorInfo(Code: "tool_call_failed", Message: "something went wrong"),
            AutoSpawnedSlot: null);

        string json = result.AsJson;

        Assert.That(json, Does.Not.Contain("\"crashReportPath\""));
    }

    [Test]
    public void Auto_spawn_side_channel_serializes_alongside_payload()
    {
        // Auto-spawn populates the side channel on success — the dispatcher uses
        // this on the very tool call that triggered the spawn. Both fields must
        // appear; payload is the tool's normal output, autoSpawnedSlot is the
        // one-shot notification.
        ReturnResult result = new(
            Payload: JsonValue.Create("done"),
            Error: null,
            AutoSpawnedSlot: new SlotInfo(
                SlotId: "armadillo",
                Version: "WIP",
                Reason: "Auto-spawned Rhino WIP to serve 'g2_search_components'."));

        string json = result.AsJson;

        Assert.That(json, Does.Contain("\"payload\""));
        Assert.That(json, Does.Contain("\"autoSpawnedSlot\""));
        Assert.That(json, Does.Contain("\"slotId\":\"armadillo\""));
        Assert.That(json, Does.Not.Contain("\"error\""));
    }
}
