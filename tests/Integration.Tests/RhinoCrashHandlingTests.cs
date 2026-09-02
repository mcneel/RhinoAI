using System.Diagnostics;
using System.Runtime.InteropServices;
using RhinoAI.Integration.Tests.Harness;

namespace RhinoAI.Integration.Tests;

// Verifies the end-to-end crash path: when a Rhino slot dies mid-call, the
// dispatcher must catch the dropped HTTP connection (ProxyDispatcher.WrapCrash)
// and surface an ErrorInfo with code "rhino_crashed" on the in-flight call,
// not let the exception escape or return a successful payload. The dead slot
// must also be reaped from the registry. Runs on both macOS and Windows — the
// handler and RhinoCrashReportFinder are platform-agnostic; only the way we
// forcibly crash the child Rhino differs (SIGSEGV vs. forced terminate), see
// CrashRhino.
[TestFixture]
public sealed class RhinoCrashHandlingTests : RouterFixture
{
    [Test]
    public async Task in_flight_call_when_rhino_crashes_returns_rhino_crashed_envelope()
    {
        ReturnResult spawn = await _router.CallToolAsync("spawn_slot", Args.Of(("version", "8")));
        Assert.That(spawn.Error, Is.Null,
            $"Precondition: spawn must succeed. {spawn.Error?.Code}: {spawn.Error?.Message}");
        string slotId = spawn.Payload!.Value.GetProperty("slotId").GetString()!;
        int pid = spawn.Payload.Value.GetProperty("pid").GetInt32();

        // Long-running script keeps the HTTP call in-flight on the router side
        // until the crash lands. 15s is comfortably longer than the kill delay
        // but short enough that a failed-to-crash run fails the test promptly.
        Task<ReturnResult> callTask = _router.CallToolAsync(
            "run_python",
            Args.Of(("slot", (object?)slotId), ("script", "import time\ntime.sleep(15)\n")));

        // Give the plugin a moment to receive the call and enter the sleep —
        // killing before the HTTP request has been forwarded would short-circuit
        // through a different (slot_not_found / probe-during-list) code path.
        await Task.Delay(2000);
        CrashRhino(pid);

        ReturnResult result = await callTask;
        Assert.That(result.Error, Is.Not.Null,
            "A crash in the target Rhino must surface as an ErrorInfo on the in-flight call, not a successful payload.");
        Assert.That(result.Error!.Code, Is.EqualTo("rhino_crashed"));
        Assert.That(result.Error.Message, Does.Contain(slotId),
            "rhino_crashed message must name the dead slot so the agent can correlate.");
        Assert.That(result.Error.Message, Does.Contain(pid.ToString()),
            "rhino_crashed message must include the dead pid for log correlation.");

        // CrashReportPath is best-effort: macOS may not have flushed the .ips
        // by the time WrapCrash runs, and a Windows forced terminate writes no
        // artifact at all (no minidump / RhinoDotNetCrash.txt), so it stays null
        // there. But when the dispatcher does attach one, it must point at a real
        // file — otherwise we're handing the agent a dead pointer.
        if (result.Error.CrashReportPath is not null)
        {
            Assert.That(File.Exists(result.Error.CrashReportPath), Is.True,
                $"crashReportPath '{result.Error.CrashReportPath}' was attached but the file is missing.");
        }

        // WrapCrash prunes the slot before returning, so a follow-up list_slots
        // must show it gone — otherwise the router is leaking dead entries.
        ReturnResult list = await _router.CallToolAsync("list_slots");
        Assert.That(list.Payload?.GetArrayLength(), Is.EqualTo(0));
    }

    // Forcibly crash the target Rhino so its HTTP listener drops mid-call. The
    // dispatcher path under test is identical on both OSes — only the kill
    // mechanism differs.
    private static void CrashRhino(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            KillWindows(pid);
        else
            KillWithSegv(pid);
    }

    // A forced terminate drops the socket, which is all the dispatcher needs to
    // see (connection reset → reap → rhino_crashed). It produces no crash
    // artifact, so crashReportPath stays null — handled best-effort above.
    private static void KillWindows(int pid)
    {
        using Process target = Process.GetProcessById(pid);
        target.Kill(entireProcessTree: true);
        target.WaitForExit();
    }

    // SIGSEGV (vs. SIGKILL) is what makes macOS write a Rhinoceros-*.ips into
    // ~/Library/Logs/DiagnosticReports — SIGKILL produces no crash report.
    // Using /bin/kill keeps us out of P/Invoke territory.
    private static void KillWithSegv(int pid)
    {
        using Process kill = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/kill",
            Arguments = $"-SEGV {pid}",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        }) ?? throw new InvalidOperationException("Failed to spawn /bin/kill");
        kill.WaitForExit();
        if (kill.ExitCode != 0)
        {
            string err = kill.StandardError.ReadToEnd();
            throw new InvalidOperationException($"kill -SEGV {pid} exited {kill.ExitCode}: {err}");
        }
    }
}
