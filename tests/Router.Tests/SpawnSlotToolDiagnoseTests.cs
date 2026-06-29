using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RhMcp.Router;
using RhMcp.Router.Tools;

namespace RhMcp.Router.Tests;

// Guards the spawn_slot-specific exception arms that SpawnDiagnostics deliberately
// declines. The status-code HttpRequestException case is a regression guard: the Mac
// listener fan-out (RhinoControlClient.SpawnListenerAsync) can throw a non-2xx
// HttpRequestException that IsConnectionFailure rejects, so SpawnDiagnostics returns
// false and this local arm must still classify it as existing_rhino_unreachable rather
// than dropping it into the generic `unexpected` bucket.
[TestFixture]
public sealed class SpawnSlotToolDiagnoseTests
{
    private static RhinoCrashReportFinder Finder => new(NullLogger<RhinoCrashReportFinder>.Instance);

    [Test]
    public void Status_code_http_failure_classifies_as_existing_rhino_unreachable()
    {
        // Non-connection HTTP failure: a 5xx from the control endpoint during the
        // Mac fan-out. IsConnectionFailure is false, so SpawnDiagnostics declines it.
        HttpRequestException hre = new("Control call _router_spawn_listener returned HTTP 500: boom");
        Assert.That(SpawnDiagnostics.TryClassify(hre, Finder, out _), Is.False);

        ErrorInfo error = SpawnSlotTool.Diagnose(hre, Finder);

        Assert.That(error.Code, Is.EqualTo("existing_rhino_unreachable"));
        Assert.That(error.Message, Does.Contain("Call spawn_slot again"));
        Assert.That(error.Message, Does.Contain("HTTP 500"));
    }

    [Test]
    public void Connection_level_http_failure_keeps_the_shared_classification()
    {
        // The connection-level shape is owned by SpawnDiagnostics; the local arm must
        // not shadow it. Same code, but the shared base message (not the status-code one).
        HttpRequestException hre = new(
            HttpRequestError.ConnectionError, "connection refused", inner: new SocketException());

        ErrorInfo error = SpawnSlotTool.Diagnose(hre, Finder);

        Assert.That(error.Code, Is.EqualTo("existing_rhino_unreachable"));
        Assert.That(error.Message, Does.Contain("stale slot has been pruned"));
        Assert.That(error.Message, Does.Contain("Call spawn_slot again"));
    }

    [Test]
    public void Cancellation_classifies_as_cancelled()
    {
        ErrorInfo error = SpawnSlotTool.Diagnose(new OperationCanceledException(), Finder);

        Assert.That(error.Code, Is.EqualTo("cancelled"));
    }

    [Test]
    public void Unknown_exception_falls_through_to_unexpected()
    {
        ErrorInfo error = SpawnSlotTool.Diagnose(new ArgumentNullException("arg"), Finder);

        Assert.That(error.Code, Is.EqualTo("unexpected"));
    }
}
