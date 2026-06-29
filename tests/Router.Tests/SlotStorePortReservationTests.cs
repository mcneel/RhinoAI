using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RhMcp.Router;

namespace RhMcp.Router.Tests;

// Regression for the concurrency finding: ReservePort used to hold _connLock AND an open
// BEGIN IMMEDIATE write transaction across the whole synchronous isPortListening probe loop,
// pinning the cross-process SQLite write lock across slow per-port network I/O and serialising
// every other router's write path behind it. The fix snapshots taken ports in a short read,
// drops the lock to probe, then takes a fresh transaction only for the final claim. These tests
// pin both that behaviour (a concurrent write proceeds while a probe is parked) and that the
// chosen port still respects DB-taken ports and the probe result.
[TestFixture]
public sealed class SlotStorePortReservationTests
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

    [Test]
    public async Task ReservePort_does_not_hold_the_write_lock_across_the_probe()
    {
        using SlotStore store = NewStore();
        store.Reserve("alpha", "8", routerPid: 1);
        store.Reserve("beta", "8", routerPid: 1);

        using ManualResetEventSlim probeEntered = new();
        using ManualResetEventSlim releaseProbe = new();

        Task<int> reserve = Task.Run(() => store.ReservePort("alpha", basePort: 5000, isPortListening: _ =>
        {
            probeEntered.Set();
            releaseProbe.Wait(TimeSpan.FromSeconds(5));
            return false;
        }));

        Assert.That(probeEntered.Wait(TimeSpan.FromSeconds(5)), Is.True, "probe was never entered");

        // With the lock held across the probe (the bug), this concurrent write path would block
        // until the probe returned. With the fix it completes promptly while the probe is parked.
        Task write = Task.Run(() => store.Delete("beta"));
        Task completed = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.That(ReferenceEquals(completed, write), Is.True,
            "a concurrent write path was blocked while ReservePort sat in its probe");
        await write;

        releaseProbe.Set();
        Assert.That(await reserve, Is.EqualTo(5000));
    }

    [Test]
    public void ReservePort_skips_ports_already_taken_in_the_db()
    {
        using SlotStore store = NewStore();
        store.Reserve("alpha", "8", routerPid: 1);
        store.Reserve("beta", "8", routerPid: 1);

        int first = store.ReservePort("alpha", basePort: 6000, isPortListening: _ => false);
        Assert.That(first, Is.EqualTo(6000));

        int second = store.ReservePort("beta", basePort: 6000, isPortListening: _ => false);
        Assert.That(second, Is.EqualTo(6001));
    }

    [Test]
    public void ReservePort_skips_ports_the_os_is_listening_on()
    {
        using SlotStore store = NewStore();
        store.Reserve("alpha", "8", routerPid: 1);

        int port = store.ReservePort("alpha", basePort: 7000, isPortListening: p => p == 7000);
        Assert.That(port, Is.EqualTo(7001));
    }
}
