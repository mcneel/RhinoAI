using Microsoft.Extensions.Logging.Abstractions;
using RhMcp.Router;
using Xunit;

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
    [Fact]
    public void TryFind_by_pid_parses_real_ips_when_available()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Logs", "DiagnosticReports");
        if (!Directory.Exists(dir)) return;
        var ips = Directory.GetFiles(dir, "Rhinoceros-*.ips")
            .OrderByDescending(p => new FileInfo(p).LastWriteTimeUtc)
            .FirstOrDefault();
        if (ips is null) return;

        // Sniff pid out of the body JSON ourselves so we can hand it to the finder.
        var text = File.ReadAllText(ips);
        var nl = text.IndexOf('\n');
        if (nl < 0) return;
        using var doc = System.Text.Json.JsonDocument.Parse(text[(nl + 1)..]);
        if (!doc.RootElement.TryGetProperty("pid", out var pidEl) || !pidEl.TryGetInt32(out var pid)) return;

        var finder = new RhinoCrashReportFinder(NullLogger<RhinoCrashReportFinder>.Instance);
        var report = finder.TryFind(pid);

        Assert.NotNull(report);
        Assert.Equal(ips, report.Path);
        Assert.NotEmpty(report.TopFrames);
        Assert.NotNull(report.Signal); // every macOS crash report has one

        // ManagedException is optional (older .ips, non-managed crashes), but
        // when present it must come with at least one managed frame — otherwise
        // we've extracted a header without the stack it belongs to.
        if (report.ManagedException is not null)
        {
            Assert.NotEmpty(report.ManagedFrames);
            // Every managed frame starts with "at " — sanity check the parse.
            Assert.All(report.ManagedFrames, f => Assert.StartsWith("at ", f));
            // Build-server paths must be stripped — the whole point of the
            // post-process step.
            Assert.DoesNotContain(report.ManagedFrames, f => f.Contains("/Users/bozo/TeamCity"));
        }
    }
}
