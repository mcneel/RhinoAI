using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace RhMcp.Router.Tests;

// Locks the wire formats the router and plugin/agent agree on. Every
// (de)serialization goes through the source-generated RouterJsonContext —
// that's what production uses and what we want to pin against AOT/trim
// regressions.
public class RouterJsonContextTests
{
    private static readonly RouterJsonContext Ctx = RouterJsonContext.Default;

    // ---- JsonRpcRequest ---------------------------------------------------

    [Test]
    public void JsonRpcRequest_serializes_with_lowercase_wire_names()
    {
        var args = new JsonObject { ["script"] = "print('hi')" };
        var rpc = new JsonRpcRequest(
            Jsonrpc: "2.0",
            Id: "abc",
            Method: "tools/call",
            Params: new JsonRpcRequestParams("run_python", args));

        var json = JsonSerializer.Serialize(rpc, Ctx.JsonRpcRequest);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("jsonrpc").GetString(), Is.EqualTo("2.0"));
        Assert.That(root.GetProperty("id").GetString(), Is.EqualTo("abc"));
        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("tools/call"));
        var p = root.GetProperty("params");
        Assert.That(p.GetProperty("name").GetString(), Is.EqualTo("run_python"));
        Assert.That(p.GetProperty("arguments").GetProperty("script").GetString(),
            Is.EqualTo("print('hi')"));
    }

    // ---- SpawnErrorPayload -------------------------------------------------

    [Test]
    public void SpawnErrorPayload_with_null_crashReport_omits_the_field()
    {
        var p = new SpawnErrorPayload("rhino_not_installed", "no rhino here");
        var json = JsonSerializer.Serialize(p, Ctx.SpawnErrorPayload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("error").GetString(), Is.EqualTo("rhino_not_installed"));
        Assert.That(root.GetProperty("message").GetString(), Is.EqualTo("no rhino here"));
        Assert.That(root.TryGetProperty("crashReport", out _), Is.False);
    }

    [Test]
    public void SpawnErrorPayload_with_crashReport_roundtrips()
    {
        var report = new RhinoCrashReport(
            Path: "/tmp/Rhinoceros-1.ips",
            CaptureTime: "2026-05-15T10:00:00Z",
            BuildVersion: "9.0.25130",
            Signal: "SIGABRT",
            Termination: "Namespace SIGNAL, Code 6",
            Asi: "NullReferenceException",
            ManagedException: "System.NullReferenceException: Object reference not set",
            ManagedFrames: ["at Foo.Bar()", "at Foo.Baz()"],
            TopFrames: ["libsystem 0x1", "RhCore 0x2"]);

        var original = new SpawnErrorPayload("rhino_crashed", "rhino is dead", report);
        var json = JsonSerializer.Serialize(original, Ctx.SpawnErrorPayload);
        var clone = JsonSerializer.Deserialize(json, Ctx.SpawnErrorPayload);

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone!.Error, Is.EqualTo("rhino_crashed"));
        Assert.That(clone.Message, Is.EqualTo("rhino is dead"));
        Assert.That(clone.CrashReport, Is.Not.Null);
        Assert.That(clone.CrashReport!.Path, Is.EqualTo("/tmp/Rhinoceros-1.ips"));
        Assert.That(clone.CrashReport.ManagedException,
            Is.EqualTo("System.NullReferenceException: Object reference not set"));
        Assert.That(clone.CrashReport.ManagedFrames,
            Is.EqualTo(new[] { "at Foo.Bar()", "at Foo.Baz()" }));
        Assert.That(clone.CrashReport.TopFrames,
            Is.EqualTo(new[] { "libsystem 0x1", "RhCore 0x2" }));
    }

    // ---- Announcement ------------------------------------------------------

    [Test]
    public void Announcement_parses_full_drop_file_shape()
    {
        var json = """{"v":1,"pid":1234,"port":5678,"version":"WIP"}""";
        var ann = JsonSerializer.Deserialize(json, Ctx.Announcement);
        Assert.That(ann, Is.Not.Null);
        Assert.That(ann!.V, Is.EqualTo(1));
        Assert.That(ann.Pid, Is.EqualTo(1234));
        Assert.That(ann.Port, Is.EqualTo(5678));
        Assert.That(ann.Version, Is.EqualTo("WIP"));
    }

    [Test]
    public void Announcement_missing_optional_version_becomes_null()
    {
        var json = """{"v":1,"pid":1234,"port":5678}""";
        var ann = JsonSerializer.Deserialize(json, Ctx.Announcement);
        Assert.That(ann, Is.Not.Null);
        Assert.That(ann!.Version, Is.Null);
    }

    [Test]
    public void Announcement_ignores_unknown_fields()
    {
        var json = """{"v":1,"pid":1234,"port":5678,"version":"9","futureField":"ignored","extra":42}""";
        var ann = JsonSerializer.Deserialize(json, Ctx.Announcement);
        Assert.That(ann, Is.Not.Null);
        Assert.That(ann!.V, Is.EqualTo(1));
        Assert.That(ann.Pid, Is.EqualTo(1234));
        Assert.That(ann.Port, Is.EqualTo(5678));
        Assert.That(ann.Version, Is.EqualTo("9"));
    }

    // ---- CloseSlotResult ---------------------------------------------------

    [Test]
    public void CloseSlotResult_happy_path_roundtrips()
    {
        var original = new CloseSlotResult(Closed: true);
        var json = JsonSerializer.Serialize(original, Ctx.CloseSlotResult);
        var clone = JsonSerializer.Deserialize(json, Ctx.CloseSlotResult);

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone!.Closed, Is.True);
        Assert.That(clone.Error, Is.Null);
        Assert.That(clone.Message, Is.Null);
    }

    [Test]
    public void CloseSlotResult_with_error_roundtrips()
    {
        var original = new CloseSlotResult(
            Closed: false,
            Error: "adopted_slot",
            Message: "user started this rhino — close it yourself");
        var json = JsonSerializer.Serialize(original, Ctx.CloseSlotResult);
        var clone = JsonSerializer.Deserialize(json, Ctx.CloseSlotResult);

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone!.Closed, Is.False);
        Assert.That(clone.Error, Is.EqualTo("adopted_slot"));
        Assert.That(clone.Message, Is.EqualTo("user started this rhino — close it yourself"));
    }
}
