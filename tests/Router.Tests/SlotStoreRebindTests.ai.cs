using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Rhino.AI.Router;

namespace Rhino.AI.Router.Tests;

// A router-spawned Rhino that replaces its listener (MCPSpawn restarting the
// auto-started one, a document swap) drops a departure and then announces the
// new listener. The slot must survive that as the same, still-owned slot,
// rather than being pruned and coming back as an adopted (uncloseable) one.
[TestFixture]
public sealed class SlotStoreRebindTests
{
    private string HomeOverride { get; set; } = "";
    private string? PreviousHome { get; set; }

    [SetUp]
    public void SetUp()
    {
        PreviousHome = Environment.GetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar);
        HomeOverride = Path.Combine(Path.GetTempPath(), "rhmcp-slotstore-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, HomeOverride);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, PreviousHome);
        try { Directory.Delete(HomeOverride, recursive: true); }
        catch { /* best effort temp cleanup */ }
    }

    private SlotStore NewStore() => new(NullLogger<SlotStore>.Instance);

    private static SlotStore Spawned(SlotStore store, string slotId, int port, int pid)
    {
        store.Reserve(slotId, "9", routerPid: 1);
        store.MarkReady(slotId, port, pid);
        return store;
    }

    [Test]
    public void Departure_with_keepOwned_leaves_a_spawned_slot()
    {
        using SlotStore store = Spawned(NewStore(), "alpha", port: 7000, pid: 42);

        Assert.That(store.DeleteByListener(42, 7000, keepOwned: true), Is.Empty);
        Assert.That(store.Get("alpha"), Is.Not.Null);
    }

    [Test]
    public void Departure_with_keepOwned_still_prunes_an_adopted_slot()
    {
        using SlotStore store = NewStore();
        string? adopted = store.AdoptIfNew("9", port: 7000, pid: 42, routerPid: 1);

        Assert.That(store.DeleteByListener(42, 7000, keepOwned: true), Is.EqualTo(new[] { adopted }));
        Assert.That(store.Get(adopted!), Is.Null);
    }

    [Test]
    public void Departure_without_keepOwned_prunes_as_before()
    {
        using SlotStore store = Spawned(NewStore(), "alpha", port: 7000, pid: 42);

        Assert.That(store.DeleteByListener(42, 7000), Is.EqualTo(new[] { "alpha" }));
        Assert.That(store.Get("alpha"), Is.Null);
    }

    [Test]
    public void Replacement_listener_rebinds_the_owned_slot_instead_of_adopting()
    {
        using SlotStore store = Spawned(NewStore(), "alpha", port: 7000, pid: 42);
        store.DeleteByListener(42, 7000, keepOwned: true);

        Assert.That(store.RebindOwned(42, 7001, isPortListening: _ => false), Is.EqualTo("alpha"));

        ChildRhino? slot = store.Get("alpha");
        Assert.That(slot?.Port, Is.EqualTo(7001));
        Assert.That(slot?.Adopted, Is.False);
        // The announcement is now a duplicate, so it adopts nothing.
        Assert.That(store.AdoptIfNew("9", port: 7001, pid: 42, routerPid: 1), Is.Null);
        Assert.That(store.ListReady(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Rebind_leaves_a_sibling_slot_whose_listener_is_still_up()
    {
        // Mac: one Rhino process, one slot per document. A new document's
        // listener must not steal a live sibling's slot.
        using SlotStore store = Spawned(NewStore(), "alpha", port: 7000, pid: 42);

        Assert.That(store.RebindOwned(42, 7001, isPortListening: p => p == 7000), Is.Null);
        Assert.That(store.Get("alpha")?.Port, Is.EqualTo(7000));
    }

    [Test]
    public void Rebind_ignores_other_processes_and_adopted_slots()
    {
        using SlotStore store = Spawned(NewStore(), "alpha", port: 7000, pid: 42);
        string? adopted = store.AdoptIfNew("9", port: 7100, pid: 43, routerPid: 1);

        Assert.That(store.RebindOwned(99, 7001, isPortListening: _ => false), Is.Null);
        Assert.That(store.RebindOwned(43, 7101, isPortListening: _ => false), Is.Null);
        Assert.That(store.Get("alpha")?.Port, Is.EqualTo(7000));
        Assert.That(store.Get(adopted!)?.Port, Is.EqualTo(7100));
    }
}
