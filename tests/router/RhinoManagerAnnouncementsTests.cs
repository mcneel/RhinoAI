using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace RhMcp.Router.Tests;

// Covers RhinoManager.ScanAnnouncements: the drop-file scan that adopts a
// user-started Rhino as a slot. Each test writes announcement JSON into its
// own temp dir and verifies the (file consumed, slot present?) outcome.
//
// Slot ids come from the global AnimalNames sequence, so every test resets it
// in [SetUp] and looks up the expected first name ("armadillo") via Get.
public class RhinoManagerAnnouncementsTests
{
    private string _dir = null!;

    [SetUp]
    public void Setup()
    {
        AnimalNames.Reset();
        _dir = Path.Combine(Path.GetTempPath(), "rhino-mcp-listeners-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* leave for OS temp cleanup */ }
    }

    private static RhinoManager NewManager()
    {
        var config = RouterConfig.FromArgs([]);
        var locator = new RhinoLocator();
        var control = new RhinoControlClient(new StubHttpClientFactory(), NullLogger<RhinoControlClient>.Instance);
        return new RhinoManager(locator, config, control, NullLogger<RhinoManager>.Instance);
    }

    [Test]
    public void Adopts_valid_announcement_when_port_is_live()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var file = Path.Combine(_dir, "ann.json");
        File.WriteAllText(file, $$"""{"v":1,"pid":12345,"port":{{port}},"version":"8"}""");

        var mgr = NewManager();
        mgr.ScanAnnouncements(_dir);

        var slot = mgr.Get("armadillo");
        Assert.That(slot, Is.Not.Null);
        Assert.That(slot!.Adopted, Is.True);
        Assert.That(slot.Pid, Is.EqualTo(12345));
        Assert.That(slot.Port, Is.EqualTo(port));
        Assert.That(slot.Version, Is.EqualTo("8"));
        // Drop-file is a one-shot doorbell — must be consumed regardless of outcome.
        Assert.That(File.Exists(file), Is.False);
    }

    [Test]
    public void Deletes_bad_json_without_throwing()
    {
        var file = Path.Combine(_dir, "bad.json");
        File.WriteAllText(file, "{ not json");

        var mgr = NewManager();
        Assert.DoesNotThrow(() => mgr.ScanAnnouncements(_dir));

        Assert.That(File.Exists(file), Is.False);
        Assert.That(mgr.Get("armadillo"), Is.Null);
    }

    [Test]
    public void Drops_announcement_when_port_is_not_listening()
    {
        // Reserve a port, immediately release it. IsPortListening's TCP connect
        // to 127.0.0.1 fails fast → announcement is stale → file deleted, no slot.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var file = Path.Combine(_dir, "stale.json");
        File.WriteAllText(file, $$"""{"v":1,"pid":99999,"port":{{closedPort}},"version":"8"}""");

        var mgr = NewManager();
        mgr.ScanAnnouncements(_dir);

        Assert.That(File.Exists(file), Is.False);
        Assert.That(mgr.Get("armadillo"), Is.Null);
    }

    [Test]
    public void Ignores_announcement_with_zero_pid_or_port()
    {
        var file = Path.Combine(_dir, "zeros.json");
        File.WriteAllText(file, """{"v":1,"pid":0,"port":0,"version":"8"}""");

        var mgr = NewManager();
        mgr.ScanAnnouncements(_dir);

        Assert.That(File.Exists(file), Is.False);
        Assert.That(mgr.Get("armadillo"), Is.Null);
    }

    [Test]
    public void Missing_directory_is_a_silent_no_op()
    {
        var mgr = NewManager();
        Assert.DoesNotThrow(() => mgr.ScanAnnouncements(Path.Combine(_dir, "does-not-exist")));
        Assert.That(mgr.Get("armadillo"), Is.Null);
    }

    // ScanAnnouncements never calls into the control client during the scan, so
    // a no-op IHttpClientFactory is enough to construct RhinoManager. If a test
    // ever exercises the spawn path, this stub will need replacing.
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
