using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Rhino.AI.Router;

namespace Rhino.AI.Router.Tests;

// CloseAsync's adopted-slot policy: refuse when the slot's process still has a
// visible top-level window (a human might be looking at it), close cooperatively
// when it doesn't (nothing left to close by hand -- the --hidden orphan case).
// Windows here never need a real process: the seeded pid is a dead one, so the
// cooperative-close fallback (control call fails, WaitForProcessExitAsync sees no
// such pid, returns immediately) never reaches Process.Kill.
[TestFixture]
public sealed class AdoptedSlotCloseTests
{
    private string _homeDir = null!;
    private string? _previousHome;
    private SlotStore _store = null!;
    private readonly int _routerPid = Environment.ProcessId;

    // Not a live process. IsProcessAlive/Process.GetProcessById treats a
    // non-existent pid as "already exited", so the cooperative-close fallback
    // in RhinoManager.CloseAsync resolves instantly without ever calling Kill.
    private const int DeadPid = 999_999;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), "rhmcp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_homeDir);
        _previousHome = Environment.GetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar);
        Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, _homeDir);
        _store = new SlotStore(NullLogger<SlotStore>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _store.Dispose();
        Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, _previousHome);
        try { Directory.Delete(_homeDir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public void Adopted_slot_with_a_visible_window_is_refused()
    {
        string slotId = SeedAdopted();
        var manager = MakeManager(new FakeWindowProbe(WindowVisibility.Visible));

        var ex = Assert.ThrowsAsync<AdoptedSlotCloseException>(
            (Func<Task>)(() => manager.CloseAsync(slotId)));

        Assert.That(ex!.SlotId, Is.EqualTo(slotId));
        Assert.That(ex.Reason, Is.EqualTo(WindowVisibility.Visible));
        Assert.That(_store.Get(slotId), Is.Not.Null, "a refused close must not drop the slot");
    }

    [Test]
    public async Task Adopted_slot_with_no_visible_window_is_closed()
    {
        string slotId = SeedAdopted();
        var manager = MakeManager(new FakeWindowProbe(WindowVisibility.Hidden));

        bool closed = await manager.CloseAsync(slotId);

        Assert.That(closed, Is.True);
        Assert.That(_store.Get(slotId), Is.Null);
    }

    [Test]
    public void Adopted_slot_with_undetermined_visibility_is_refused()
    {
        // Mirrors macOS, which has no probe: Unknown must refuse, same as before
        // this change, so the mac adoption policy is unchanged.
        string slotId = SeedAdopted();
        var manager = MakeManager(new FakeWindowProbe(WindowVisibility.Unknown));

        var ex = Assert.ThrowsAsync<AdoptedSlotCloseException>(
            (Func<Task>)(() => manager.CloseAsync(slotId)));

        Assert.That(ex!.Reason, Is.EqualTo(WindowVisibility.Unknown));
        Assert.That(_store.Get(slotId), Is.Not.Null);
    }

    [Test]
    public async Task Non_adopted_slot_closes_as_before_without_consulting_the_probe()
    {
        (_, string slotId) = _store.ReserveNewNamed("8", _routerPid);
        _store.MarkReady(slotId, port: 11500, pid: DeadPid);
        var probe = new FakeWindowProbe(WindowVisibility.Visible); // would refuse if it were ever asked
        var manager = MakeManager(probe);

        bool closed = await manager.CloseAsync(slotId);

        Assert.That(closed, Is.True);
        Assert.That(probe.Calls, Is.EqualTo(0), "a non-adopted slot must not consult the window probe");
    }

    private string SeedAdopted()
    {
        string? id = _store.AdoptIfNew("8", port: 11500, pid: DeadPid, routerPid: _routerPid);
        Assert.That(id, Is.Not.Null);
        return id!;
    }

    private RhinoManager MakeManager(IWindowProbe probe)
    {
        RhinoControlClient control = new(new StubHttpClientFactory(), NullLogger<RhinoControlClient>.Instance);
        return new RhinoManager(
            RouterConfig.FromArgs([]), control, _store, NullLogger<RhinoManager>.Instance, probe);
    }

    private sealed class FakeWindowProbe(WindowVisibility result) : IWindowProbe
    {
        public int Calls { get; private set; }
        public WindowVisibility Probe(int pid) { Calls++; return result; }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        // The cooperative-close path posts to http://localhost:<port>, which
        // nothing is listening on here; the real HttpClient's connection-refused
        // failure is caught by CloseAsync and logged, same as a genuinely
        // unreachable Rhino.
        public HttpClient CreateClient(string name) => new();
    }
}
