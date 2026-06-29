using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RhMcp.Router;

namespace RhMcp.Router.Tests;

// Regression for the sentinel finding: a launching placeholder INSERT omits port/pid, and
// ReadRow used to map the DBNull columns to 0, conflating "not yet assigned" with a real 0
// and materialising a lying "http://localhost:0" endpoint. Port/Pid are now int?, so a
// launching row carries genuine absence and Endpoint refuses to address an unbound slot.
[TestFixture]
public sealed class SlotStoreLaunchingAbsenceTests
{
    private string HomeOverride { get; set; } = "";
    private string? PreviousHome { get; set; }

    [SetUp]
    public void SetUp()
    {
        PreviousHome = Environment.GetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar);
        HomeOverride = Path.Combine(Path.GetTempPath(), "rhmcp-slotstore-launch-test-" + Guid.NewGuid().ToString("N"));
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
    public void Launching_row_has_no_port_or_pid()
    {
        using SlotStore store = NewStore();
        store.Reserve("alpha", "8", routerPid: 1);

        ChildRhino? row = store.Get("alpha");
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Port, Is.Null);
        Assert.That(row.Pid, Is.Null);
        Assert.That(row.Status, Is.EqualTo(SlotStatus.Launching));
    }

    [Test]
    public void Launching_row_endpoint_is_not_addressable()
    {
        using SlotStore store = NewStore();
        store.Reserve("alpha", "8", routerPid: 1);

        ChildRhino row = store.Get("alpha")!;
        Assert.Throws<InvalidOperationException>((Action)(() => { _ = row.Endpoint; }));
    }

    [Test]
    public void Ready_row_carries_port_pid_and_addressable_endpoint()
    {
        using SlotStore store = NewStore();
        store.Reserve("alpha", "8", routerPid: 1);
        store.MarkReady("alpha", port: 10500, pid: 4321);

        ChildRhino row = store.Get("alpha")!;
        Assert.That(row.Port, Is.EqualTo(10500));
        Assert.That(row.Pid, Is.EqualTo(4321));
        Assert.That(row.Endpoint, Is.EqualTo("http://localhost:10500"));
    }
}
