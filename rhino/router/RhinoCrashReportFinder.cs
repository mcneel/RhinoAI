using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

// When a child Rhino crashes, the router only sees "connection refused" — the
// HTTP channel went down with the process. macOS, however, writes a crash
// report to ~/Library/Logs/DiagnosticReports/Rhinoceros-*.ips a few seconds
// later. This class digs the latest one out and extracts the agent-useful
// parts (signal, abort reason, top of the faulting stack) so we can surface
// the actual crash cause instead of just "it died".
//
// macOS-only. Windows (WER / Rhino's own crash dumps) is a TODO.
public class RhinoCrashReportFinder(ILogger<RhinoCrashReportFinder> log)
{
    private const int MaxFrames = 12;
    private static readonly TimeSpan FuzzyMatchWindow = TimeSpan.FromMinutes(5);

    // Most-recent-within-FuzzyMatchWindow. Use when we know "a Rhino just died"
    // but don't have the specific pid plumbed through (e.g. the failure surfaced
    // as an HttpRequestException several call layers up from the lead's record).
    public RhinoCrashReport? TryFindMostRecent() => TryFind(pid: null);

    // Look for the .ips matching this pid first; if none does (the report may
    // not be written yet, or the pid was reused), fall back to the most recent
    // Rhinoceros-*.ips inside FuzzyMatchWindow. Pass `null` to skip the pid-match
    // phase entirely. Returns null on Windows, when the directory is missing,
    // or when nothing plausible is found.
    public RhinoCrashReport? TryFind(int? pid)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return null;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Logs", "DiagnosticReports");
        if (!Directory.Exists(dir)) return null;

        FileInfo[] candidates;
        try
        {
            candidates = new DirectoryInfo(dir)
                .GetFiles("Rhinoceros-*.ips")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToArray();
        }
        catch (Exception ex)
        {
            log.LogDebug("Failed to list crash reports in {Dir}: {Error}", dir, ex.Message);
            return null;
        }
        if (candidates.Length == 0) return null;

        // Phase 1: pid match wins. Walk newest→oldest so we don't grab a
        // recycled-pid match from days ago. Skipped when caller passes null.
        if (pid is int wantPid)
        {
            foreach (var fi in candidates)
            {
                var parsed = TryParse(fi.FullName);
                if (parsed is null) continue;
                if (parsed.MatchedPid == wantPid) return parsed.Report;
            }
        }

        // Phase 2: fuzzy fallback. The most recent Rhino crash within the last
        // few minutes is overwhelmingly likely to be ours, especially when the
        // .ips for the specific pid hasn't finished writing yet.
        var cutoff = DateTime.UtcNow - FuzzyMatchWindow;
        foreach (var fi in candidates)
        {
            if (fi.LastWriteTimeUtc < cutoff) break;
            var parsed = TryParse(fi.FullName);
            if (parsed is null) continue;
            log.LogDebug("Crash report pid match for {Pid} not found; falling back to most recent ({Path})",
                pid?.ToString() ?? "(unspecified)", fi.FullName);
            return parsed.Report;
        }
        return null;
    }

    // .ips format: line 1 is a small JSON header, the rest of the file is the
    // full crash-report JSON body. Both are valid JSON in isolation. We use
    // JsonDocument (no source-gen) because we're navigating an external,
    // open-ended schema — typed deserialization would be brittle.
    private ParsedReport? TryParse(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            var newline = text.IndexOf('\n');
            if (newline < 0) return null;
            var header = text[..newline];
            var body = text[(newline + 1)..];

            string? build = null;
            try
            {
                using var hdoc = JsonDocument.Parse(header);
                if (hdoc.RootElement.TryGetProperty("build_version", out var bv))
                    build = bv.GetString();
            }
            catch { /* header parse failure isn't fatal — body has what we need */ }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            int? pid = root.TryGetProperty("pid", out var pidEl) && pidEl.TryGetInt32(out var p) ? p : null;
            string? captureTime = root.TryGetProperty("captureTime", out var ct) ? ct.GetString() : null;

            string? signal = null;
            if (root.TryGetProperty("exception", out var ex) && ex.TryGetProperty("signal", out var sigEl))
                signal = sigEl.GetString();

            string? termIndicator = null;
            if (root.TryGetProperty("termination", out var term) && term.TryGetProperty("indicator", out var ind))
                termIndicator = ind.GetString();

            string? asi = ExtractAsi(root);
            string[] topFrames = ExtractTopFrames(root);
            var (managedException, managedFrames) = ExtractManagedException(root);

            return new ParsedReport(pid, new RhinoCrashReport(
                Path: path,
                CaptureTime: captureTime,
                BuildVersion: build,
                Signal: signal,
                Termination: termIndicator,
                Asi: asi,
                ManagedException: managedException,
                ManagedFrames: managedFrames,
                TopFrames: topFrames));
        }
        catch (Exception ex)
        {
            log.LogDebug("Failed to parse crash report at {Path}: {Error}", path, ex.Message);
            return null;
        }
    }

    // ASI ("application-specific information") is where the abort reason lives,
    // e.g. {"libsystem_c.dylib": ["abort() called"]}. Flatten to "image: line"
    // strings joined with `; ` — short, scannable, and preserves the source
    // image so a managed-exception ASI ("CoreCLR: ...") is recognisable.
    private static string? ExtractAsi(JsonElement root)
    {
        if (!root.TryGetProperty("asi", out var asi) || asi.ValueKind != JsonValueKind.Object) return null;
        var lines = new List<string>();
        foreach (var prop in asi.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var v in prop.Value.EnumerateArray())
            {
                if (v.ValueKind == JsonValueKind.String)
                    lines.Add($"{prop.Name}: {v.GetString()}");
            }
        }
        return lines.Count == 0 ? null : string.Join("; ", lines);
    }

    // Top N frames of the faulting thread, formatted as "<image>  <symbol>".
    // Falls back to "+0xOFFSET" when the frame has no symbol (system libs,
    // stripped binaries). 12 frames is enough to see signal handler →
    // managed-exception bridge → AppKit/RhCore in the typical crash shape.
    private static string[] ExtractTopFrames(JsonElement root)
    {
        var frames = new List<string>();
        int faultingIdx = 0;
        if (root.TryGetProperty("faultingThread", out var ftEl) && ftEl.TryGetInt32(out var fi))
            faultingIdx = fi;

        if (!root.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        if (faultingIdx < 0 || faultingIdx >= threads.GetArrayLength()) return Array.Empty<string>();

        JsonElement[] images = Array.Empty<JsonElement>();
        if (root.TryGetProperty("usedImages", out var usedImg) && usedImg.ValueKind == JsonValueKind.Array)
            images = usedImg.EnumerateArray().ToArray();

        var thread = threads[faultingIdx];
        if (!thread.TryGetProperty("frames", out var framesEl) || framesEl.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        int n = 0;
        foreach (var frame in framesEl.EnumerateArray())
        {
            if (n++ >= MaxFrames) break;
            string imgName = "?";
            if (frame.TryGetProperty("imageIndex", out var iidx) && iidx.TryGetInt32(out var ii)
                && ii >= 0 && ii < images.Length
                && images[ii].TryGetProperty("name", out var nm))
            {
                imgName = nm.GetString() ?? "?";
            }
            string symbol;
            if (frame.TryGetProperty("symbol", out var symEl) && symEl.ValueKind == JsonValueKind.String)
            {
                symbol = symEl.GetString() ?? "?";
            }
            else if (frame.TryGetProperty("imageOffset", out var offEl) && offEl.TryGetInt64(out var off))
            {
                symbol = $"+0x{off:X}";
            }
            else
            {
                symbol = "?";
            }
            frames.Add($"{imgName}  {symbol}");
        }
        return frames.ToArray();
    }

    // Pulls the actual managed exception out of `asiBacktraces`. The shape is a
    // single string per entry, looking like:
    //
    //   [ERROR] FATAL UNHANDLED EXCEPTION: <Type>: <message>
    //     File "...", line N, in <symbol>   <-- optional Python source line
    //      at <Frame1> in /Users/bozo/TeamCity/.../File.cs:line 49
    //      at <Frame2> in /Users/bozo/TeamCity/.../File.cs:line 751
    //      ...
    //      --- End of stack trace from previous location ---
    //      at ObjCRuntime.Runtime.InvokeMethod(...)
    //   [END ERROR]
    //   [LOADED ASSEMBLIES]
    //   ...
    //
    // Returns (exceptionLine, frames). Frames are capped and have their TeamCity
    // build-machine paths stripped — those are pure noise that bloat the payload.
    // Both halves may be null/empty if `asiBacktraces` isn't present or doesn't
    // contain the expected markers (older Rhinos / non-managed crashes).
    private static (string? Exception, string[] Frames) ExtractManagedException(JsonElement root)
    {
        const int MaxManagedFrames = 10;

        if (!root.TryGetProperty("asiBacktraces", out var ab) || ab.ValueKind != JsonValueKind.Array)
            return (null, Array.Empty<string>());

        // Concatenate all entries; in practice there's only one, but be defensive.
        var sb = new System.Text.StringBuilder();
        foreach (var entry in ab.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String) continue;
            sb.AppendLine(entry.GetString());
        }
        var text = sb.ToString();
        if (text.Length == 0) return (null, Array.Empty<string>());

        // Scope to the FATAL UNHANDLED EXCEPTION block — everything after
        // [END ERROR] is loaded-assemblies noise we don't want frame matches in.
        var fatalIdx = text.IndexOf("[ERROR] FATAL UNHANDLED EXCEPTION:", StringComparison.Ordinal);
        if (fatalIdx < 0) return (null, Array.Empty<string>());
        var endIdx = text.IndexOf("[END ERROR]", fatalIdx, StringComparison.Ordinal);
        var scope = endIdx > fatalIdx ? text[fatalIdx..endIdx] : text[fatalIdx..];

        string? exceptionLine = null;
        var frames = new List<string>();
        foreach (var raw in scope.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            if (exceptionLine is null && line.StartsWith("[ERROR] FATAL UNHANDLED EXCEPTION:", StringComparison.Ordinal))
            {
                exceptionLine = line["[ERROR] FATAL UNHANDLED EXCEPTION:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("at ", StringComparison.Ordinal) && frames.Count < MaxManagedFrames)
            {
                frames.Add(StripBuildPath(line));
            }
        }

        return (exceptionLine, frames.ToArray());
    }

    // Drop the " in /Users/bozo/TeamCity/..." suffix that .NET inserts when
    // exceptions are thrown from a pdb-bearing assembly. The build-server path
    // is noise; the agent only needs the symbol chain. Preserves source file +
    // line when it can be cleanly recovered.
    private static string StripBuildPath(string atLine)
    {
        var inIdx = atLine.IndexOf(" in /", StringComparison.Ordinal);
        if (inIdx < 0) inIdx = atLine.IndexOf(" in C:\\", StringComparison.Ordinal);
        if (inIdx < 0) return atLine;

        var head = atLine[..inIdx];
        var tail = atLine[(inIdx + " in ".Length)..];

        // Tail looks like "/Users/bozo/.../File.cs:line 49" — keep just the
        // filename and line.
        var lineMarker = tail.LastIndexOf(":line ", StringComparison.Ordinal);
        var fileNameStart = Math.Max(tail.LastIndexOf('/'), tail.LastIndexOf('\\')) + 1;
        if (lineMarker > fileNameStart)
        {
            var fileName = tail[fileNameStart..lineMarker];
            var lineNum = tail[(lineMarker + ":line ".Length)..].Trim();
            return $"{head} ({fileName}:{lineNum})";
        }
        return head;
    }

    private sealed record ParsedReport(int? MatchedPid, RhinoCrashReport Report);
}
