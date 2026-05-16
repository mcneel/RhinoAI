using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace RhMcp.Router.Tests;

// Covers ProxyDispatcher's pure helpers (version compat, connection-failure
// classifier, JSON-RPC + SSE extraction, exception → SpawnErrorPayload mapping).
// All targets are internal static so this test project can reach them via
// InternalsVisibleTo("Router.Tests").
public class ProxyDispatcherTests
{
    // ---- IsVersionCompatible ----------------------------------------------

    [TestCase("8", "8")]
    [TestCase("9", "9")]
    [TestCase("WIP", "WIP")]
    public void IsVersionCompatible_returns_true_for_equal(string a, string b)
    {
        Assert.That(ProxyDispatcher.IsVersionCompatible(a, b), Is.True);
    }

    [TestCase("9", "WIP")]
    [TestCase("WIP", "9")]
    public void IsVersionCompatible_treats_9_and_WIP_as_compatible(string a, string b)
    {
        Assert.That(ProxyDispatcher.IsVersionCompatible(a, b), Is.True);
    }

    [TestCase("8", "WIP")]
    [TestCase("WIP", "8")]
    [TestCase("", "8")]
    public void IsVersionCompatible_returns_false_for_incompatible(string a, string b)
    {
        Assert.That(ProxyDispatcher.IsVersionCompatible(a, b), Is.False);
    }

    // ---- IsConnectionFailure ----------------------------------------------

    [Test]
    public void IsConnectionFailure_true_for_HttpRequestError_ConnectionError()
    {
        var ex = new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        Assert.That(ProxyDispatcher.IsConnectionFailure(ex), Is.True);
    }

    [Test]
    public void IsConnectionFailure_true_when_inner_is_SocketException()
    {
        var inner = new SocketException();
        var ex = new HttpRequestException("wrapper", inner);
        Assert.That(ProxyDispatcher.IsConnectionFailure(ex), Is.True);
    }

    [Test]
    public void IsConnectionFailure_true_when_inner_is_IOException()
    {
        var inner = new IOException("reset");
        var ex = new HttpRequestException("wrapper", inner);
        Assert.That(ProxyDispatcher.IsConnectionFailure(ex), Is.True);
    }

    [Test]
    public void IsConnectionFailure_false_for_plain_HttpRequestException()
    {
        // A non-connection HttpRequestException (no error code, no inner) is the
        // "plugin returned HTTP 5xx" shape — must not be misclassified.
        var ex = new HttpRequestException("plain");
        Assert.That(ProxyDispatcher.IsConnectionFailure(ex), Is.False);
    }

    // ---- ExtractResult / ExtractFromJsonRpc -------------------------------

    [Test]
    public void ExtractFromJsonRpc_returns_raw_result_text()
    {
        var body = """{"jsonrpc":"2.0","id":"x","result":{"content":[{"type":"text","text":"hi"}]}}""";
        var got = ProxyDispatcher.ExtractFromJsonRpc(body, "slot", "tool");
        Assert.That(got, Is.EqualTo("""{"content":[{"type":"text","text":"hi"}]}"""));
    }

    [Test]
    public void ExtractFromJsonRpc_throws_on_error_envelope()
    {
        var body = """{"jsonrpc":"2.0","id":"x","error":{"code":-32601,"message":"unknown tool"}}""";
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProxyDispatcher.ExtractFromJsonRpc(body, "slotA", "toolB"));
        Assert.That(ex!.Message, Does.Contain("slotA"));
        Assert.That(ex.Message, Does.Contain("toolB"));
        Assert.That(ex.Message, Does.Contain("unknown tool"));
    }

    [Test]
    public void ExtractFromJsonRpc_throws_when_neither_result_nor_error()
    {
        var body = """{"jsonrpc":"2.0","id":"x"}""";
        Assert.Throws<InvalidOperationException>(
            () => ProxyDispatcher.ExtractFromJsonRpc(body, "slot", "tool"));
    }

    [Test]
    public void ExtractResult_handles_single_data_sse_line()
    {
        var body = "data: {\"jsonrpc\":\"2.0\",\"id\":\"x\",\"result\":{\"ok\":true}}\n";
        var got = ProxyDispatcher.ExtractResult(body, "slot", "tool");
        Assert.That(got, Is.EqualTo("""{"ok":true}"""));
    }

    [Test]
    public void ExtractResult_handles_multiline_sse_with_event_preamble()
    {
        var body = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":\"x\",\"result\":42}\n";
        var got = ProxyDispatcher.ExtractResult(body, "slot", "tool");
        Assert.That(got, Is.EqualTo("42"));
    }

    [Test]
    public void ExtractResult_handles_crlf_line_endings()
    {
        var body = "event: message\r\ndata: {\"jsonrpc\":\"2.0\",\"id\":\"x\",\"result\":\"ok\"}\r\n";
        var got = ProxyDispatcher.ExtractResult(body, "slot", "tool");
        Assert.That(got, Is.EqualTo("\"ok\""));
    }

    [Test]
    public void ExtractResult_throws_when_sse_has_no_data_payload()
    {
        // event: line followed by an empty data: — finder must drop through and
        // throw with the full body in the message so we can diagnose live.
        var body = "event: ping\ndata:\n";
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProxyDispatcher.ExtractResult(body, "slotA", "toolB"));
        Assert.That(ex!.Message, Does.Contain("slotA"));
        Assert.That(ex.Message, Does.Contain("toolB"));
        Assert.That(ex.Message, Does.Contain("event: ping"));
    }

    // ---- DiagnoseFailure --------------------------------------------------
    //
    // Source mapping (read from ProxyDispatcher.cs):
    //   FileNotFoundException          → "rhino_not_installed"
    //   TimeoutException               → "startup_timeout"
    //   PlatformNotSupportedException  → "unsupported_platform"
    //   HttpRequestException (conn)    → "existing_rhino_unreachable"
    //   HttpRequestException (other)   → "plugin_http_error"
    //   InvalidOperationException      → "tool_call_failed"
    //   anything else                  → "unexpected"
    // (SlotNotFoundException is private to ProxyDispatcher so not covered here.)

    private static RhinoCrashReportFinder NewFinder() =>
        new(NullLogger<RhinoCrashReportFinder>.Instance);

    [Test]
    public void DiagnoseFailure_FileNotFoundException_maps_to_rhino_not_installed()
    {
        var p = ProxyDispatcher.DiagnoseFailure(
            new FileNotFoundException("missing"), child: null, "do_thing", NewFinder());
        Assert.That(p.Error, Is.EqualTo("rhino_not_installed"));
    }

    [Test]
    public void DiagnoseFailure_TimeoutException_maps_to_startup_timeout()
    {
        var p = ProxyDispatcher.DiagnoseFailure(
            new TimeoutException("slow"), child: null, "do_thing", NewFinder());
        Assert.That(p.Error, Is.EqualTo("startup_timeout"));
    }

    [Test]
    public void DiagnoseFailure_PlatformNotSupportedException_maps_to_unsupported_platform()
    {
        var p = ProxyDispatcher.DiagnoseFailure(
            new PlatformNotSupportedException("no"), child: null, "do_thing", NewFinder());
        Assert.That(p.Error, Is.EqualTo("unsupported_platform"));
    }

    [Test]
    public void DiagnoseFailure_connection_HttpRequestException_maps_to_existing_rhino_unreachable()
    {
        var hre = new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        var p = ProxyDispatcher.DiagnoseFailure(hre, child: null, "do_thing", NewFinder());
        Assert.That(p.Error, Is.EqualTo("existing_rhino_unreachable"));
    }

    [Test]
    public void DiagnoseFailure_non_connection_HttpRequestException_maps_to_plugin_http_error()
    {
        var hre = new HttpRequestException("HTTP 500");
        var p = ProxyDispatcher.DiagnoseFailure(hre, child: null, "do_thing", NewFinder());
        Assert.That(p.Error, Is.EqualTo("plugin_http_error"));
    }

    [Test]
    public void DiagnoseFailure_InvalidOperationException_maps_to_tool_call_failed()
    {
        var p = ProxyDispatcher.DiagnoseFailure(
            new InvalidOperationException("bad"), child: null, "do_thing", NewFinder());
        Assert.That(p.Error, Is.EqualTo("tool_call_failed"));
    }

    [Test]
    public void DiagnoseFailure_unknown_exception_maps_to_unexpected()
    {
        var p = ProxyDispatcher.DiagnoseFailure(
            new ApplicationException("???"), child: null, "do_thing", NewFinder());
        Assert.That(p.Error, Is.EqualTo("unexpected"));
        Assert.That(p.Message, Does.Contain("ApplicationException"));
    }
}
