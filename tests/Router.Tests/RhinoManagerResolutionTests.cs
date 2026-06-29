using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RhMcp.Router;

namespace RhMcp.Router.Tests;

// Exercises GetOrCreateDefaultAsync, the slot-less resolution ladder
// (sticky → already-open → own-spawn → cold start). Every reuse branch returns
// before SpawnAsync, so no real Rhino is launched. The one spawn path we hit is
// driven with an uninstalled version so RhinoLocator fails fast (no process).
//
// Isolation: a per-test RHINO_MCP_HOME makes ScanAnnouncements read an empty
// listeners dir, and the SlotStore uses a temp db under it. [SetUp]/[TearDown]
// run around each test, so each gets a clean home + store.
[TestFixture]
public sealed class RhinoManagerResolutionTests
{
    private string _homeDir = null!;
    private string? _previousHome;
    private SlotStore _store = null!;
    private RhinoManager _manager = null!;
    private readonly int _routerPid = Environment.ProcessId;
    private int _nextPort = 11000;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), "rhmcp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_homeDir);
        _previousHome = Environment.GetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar);
        Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, _homeDir);

        _store = new SlotStore(Path.Combine(_homeDir, "state.db"), NullLogger<SlotStore>.Instance);
        RhinoControlClient control = new(new StubHttpClientFactory(), NullLogger<RhinoControlClient>.Instance);
        _manager = new RhinoManager(
            new RhinoLocator(), RouterConfig.FromArgs([]), control, _store, NullLogger<RhinoManager>.Instance);
    }

    [Test]
    public async Task Sticky_slot_wins_over_a_newer_open_rhino()
    {
        string own = SeedOwnReady("8");
        SeedAdopted("9"); // opened later, sticky should still win
        _manager.SetActiveSlot(own);

        (ChildRhino child, bool spawned) = await _manager.GetOrCreateDefaultAsync();

        Assert.That(child.SlotId, Is.EqualTo(own));
        Assert.That(spawned, Is.False);
    }

    [Test]
    public async Task Uses_the_open_user_rhino_when_nothing_is_active()
    {
        string adopted = SeedAdopted("9");

        (ChildRhino child, bool spawned) = await _manager.GetOrCreateDefaultAsync();

        Assert.That(child.SlotId, Is.EqualTo(adopted));
        Assert.That(child.Adopted, Is.True);
        Assert.That(spawned, Is.False);
    }

    [Test]
    public async Task Prefers_the_oldest_open_rhino()
    {
        string first = SeedAdopted("8");
        SeedAdopted("9");

        (ChildRhino child, bool _) = await _manager.GetOrCreateDefaultAsync();

        Assert.That(child.SlotId, Is.EqualTo(first));
    }

    [Test]
    public async Task Falls_back_to_this_sessions_own_spawn_when_nothing_is_open()
    {
        string own = SeedOwnReady("8");

        (ChildRhino child, bool spawned) = await _manager.GetOrCreateDefaultAsync();

        Assert.That(child.SlotId, Is.EqualTo(own));
        Assert.That(spawned, Is.False);
    }

    [Test]
    public async Task Stale_active_pointer_self_heals()
    {
        _manager.SetActiveSlot("ghost"); // points at a slot that doesn't exist
        string adopted = SeedAdopted("9");

        (ChildRhino child, bool _) = await _manager.GetOrCreateDefaultAsync();

        Assert.That(child.SlotId, Is.EqualTo(adopted));
    }

    [Test]
    public async Task Gh2_reuses_an_open_wip_announced_as_9()
    {
        string adopted = SeedAdopted("9"); // user-opened WIP announces its major

        (ChildRhino child, bool spawned) = await _manager.GetOrCreateDefaultAsync(requiredVersion: "WIP");

        Assert.That(child.SlotId, Is.EqualTo(adopted));
        Assert.That(spawned, Is.False);
    }

    [Test]
    public void Pinned_call_does_not_reuse_an_incompatible_open_rhino()
    {
        // An open Rhino 8 must not satisfy a call that needs another version, so
        // the router falls through to spawn. An uninstalled version makes the
        // spawn fail fast (locator), proving the 8 was not handed back.
        SeedAdopted("8");

        Assert.ThrowsAsync<FileNotFoundException>(
            (Func<Task>)(() => _manager.GetOrCreateDefaultAsync(requiredVersion: "nope-not-installed")));
    }

    private string SeedAdopted(string version)
    {
        int port = _nextPort++;
        string? id = _store.AdoptIfNew(version, port, pid: _routerPid, routerPid: _routerPid);
        Assert.That(id, Is.Not.Null);
        return id!;
    }

    private string SeedOwnReady(string version)
    {
        (_, string id) = _store.ReserveNewNamed(version, _routerPid);
        _store.MarkReady(id, port: _nextPort++, pid: _routerPid);
        return id;
    }

    [TearDown]
    public void TearDown()
    {
        _store.Dispose();
        Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, _previousHome);
        try { Directory.Delete(_homeDir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        // Never invoked on the reuse paths under test; the spawn path fails at the
        // locator before any HTTP. Present only to satisfy the ctor.
        public HttpClient CreateClient(string name) => new();
    }
}
