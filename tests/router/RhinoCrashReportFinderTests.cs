using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RhMcp.Router;

namespace RhMcp.Router.Tests;

public class RhinoCrashReportFinderTests
{
    // Smoke test against the user's actual macOS crash reports directory if it
    // exists and has at least one Rhino .ips. Skipped on Windows / when there
    // are no reports — the parser's correctness is what we're checking, not
    // that crashes have happened.
    //
    // Pid-match path has no time window, so we can verify parsing against any
    // historical .ips by reading its pid from the file directly and asking the
    // finder to look it up.
    [Test]
    public void TryFind_by_pid_parses_real_ips_when_available()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Logs", "DiagnosticReports");
        if (!Directory.Exists(dir)) Assert.Ignore("DiagnosticReports directory not present");
        var ips = Directory.GetFiles(dir, "Rhinoceros-*.ips")
            .OrderByDescending(p => new FileInfo(p).LastWriteTimeUtc)
            .FirstOrDefault();
        if (ips is null) Assert.Ignore("No Rhinoceros-*.ips available");

        // Sniff pid out of the body JSON ourselves so we can hand it to the finder.
        var text = File.ReadAllText(ips);
        var nl = text.IndexOf('\n');
        if (nl < 0) Assert.Ignore("Malformed .ips header");
        using var doc = System.Text.Json.JsonDocument.Parse(text[(nl + 1)..]);
        int pid = -1;
        if (!doc.RootElement.TryGetProperty("pid", out var pidEl) || !pidEl.TryGetInt32(out pid))
            Assert.Ignore(".ips body missing pid");

        var finder = new RhinoCrashReportFinder(NullLogger<RhinoCrashReportFinder>.Instance);
        var report = finder.TryFind(pid);

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.Path, Is.EqualTo(ips));
        Assert.That(report.TopFrames, Is.Not.Empty);
        Assert.That(report.Signal, Is.Not.Null); // every macOS crash report has one

        // ManagedException is optional (older .ips, non-managed crashes), but
        // when present it must come with at least one managed frame — otherwise
        // we've extracted a header without the stack it belongs to.
        if (report.ManagedException is not null)
        {
            Assert.That(report.ManagedFrames, Is.Not.Empty);
            // Every managed frame starts with "at " — sanity check the parse.
            Assert.That(report.ManagedFrames, Has.All.StartsWith("at "));
            // Build-server paths must be stripped — the whole point of the
            // post-process step.
            Assert.That(report.ManagedFrames, Has.None.Contains("/Users/bozo/TeamCity"));
        }
    }
}
