using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RhinoAI.Router;

// When a child Rhino crashes, the router only sees "connection refused" — the
// HTTP channel went down with the process. The OS (and Rhino itself) writes a
// crash artifact a few seconds later: .ips on macOS; on Windows either a
// RhinoDotNetCrash.txt on the desktop (for unhandled CLR exceptions) or a
// minidump under McNeel\Rhinoceros\... / WER LocalDumps. This class digs the
// latest one out and extracts the agent-useful parts so we can surface the
// actual crash cause instead of just "it died".
public class RhinoCrashReportFinder(ILogger<RhinoCrashReportFinder> log)
{
    private const int MaxFrames = 12;
    private const uint MinidumpSignature = 0x504D444D; // 'MDMP'
    private const uint MinidumpStreamException = 6;
    private const uint MinidumpStreamMiscInfo = 15;
    private const uint MiscInfo1ProcessId = 0x1;
    private static readonly TimeSpan FuzzyMatchWindow = TimeSpan.FromMinutes(5);

    // Most-recent-within-FuzzyMatchWindow. Use when we know "a Rhino just died"
    // but don't have the specific pid plumbed through (e.g. the failure surfaced
    // as an HttpRequestException several call layers up from the lead's record).
    public RhinoCrashReport? TryFindMostRecent() => TryFind(pid: null);

    // Look for the crash artifact matching this pid first; if none does (the
    // report may not be written yet, or the pid was reused), fall back to the
    // most recent Rhino crash inside FuzzyMatchWindow. Pass `null` to skip the
    // pid-match phase entirely. Returns null when the platform isn't supported,
    // the artifact directory is missing, or nothing plausible is found.
    public RhinoCrashReport? TryFind(int? pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return TryFindMac(pid);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return TryFindWindows(pid);
        return null;
    }

    private RhinoCrashReport? TryFindMac(int? pid)
    {
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
        return SelectReport(candidates, pid, TryParseIps);
    }

    // Windows: priority order is RhinoDotNetCrash.txt → Rhino's own crash dump
    // folders → Windows Error Reporting LocalDumps. The .txt is checked first
    // because it carries the actual managed exception type, message, and stack —
    // the agent-actionable shape that .dmp parsing without symbols can't recover.
    // .dmps from Rhino's CrashDumper.exe are next; they exist whether or not the
    // user has configured WER LocalDumps in the registry. WER is the last resort.
    private RhinoCrashReport? TryFindWindows(int? pid)
    {
        RhinoCrashReport? dotnet = TryFindRhinoDotNetCrash();
        if (dotnet is not null) return dotnet;

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local)) return null;

        List<FileInfo> candidates = new();
        AppendRhinoCrashDumps(candidates, Path.Combine(local, "McNeel", "Rhinoceros"));
        AppendDumpDir(candidates, Path.Combine(local, "CrashDumps"), "Rhino*.dmp");
        if (candidates.Count == 0) return null;

        candidates.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
        return SelectReport(candidates.ToArray(), pid, TryParseMinidump);
    }

    // RhinoDotNetCrash.txt is what Rhino's managed UnhandledException handler
    // writes when a CLR exception escapes the message loop. There's only ever
    // one (Rhino overwrites it on each crash) and it has no pid, so the only
    // useful check is "was it written recently". The fuzzy window's "most recent
    // crash is overwhelmingly likely to be ours" assumption applies — same as
    // the macOS fuzzy fallback.
    private RhinoCrashReport? TryFindRhinoDotNetCrash()
    {
        foreach (string dir in EnumerateDesktopDirs())
        {
            string path = Path.Combine(dir, "RhinoDotNetCrash.txt");
            if (!File.Exists(path)) continue;

            FileInfo info;
            try { info = new FileInfo(path); }
            catch { continue; }
            if (DateTime.UtcNow - info.LastWriteTimeUtc > FuzzyMatchWindow) continue;

            RhinoCrashReport? report = TryParseDotNetCrash(path);
            if (report is not null) return report;
        }
        return null;
    }

    // SpecialFolder.Desktop follows OneDrive redirection, but Rhino has been
    // known to write to the literal %USERPROFILE%\Desktop regardless. Probe
    // both so we don't miss the file under either layout.
    private static IEnumerable<string> EnumerateDesktopDirs()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string special = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(special) && seen.Add(special)) yield return special;

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            string fallback = Path.Combine(profile, "Desktop");
            if (seen.Add(fallback)) yield return fallback;
        }
    }

    // RhinoDotNetCrash.txt is just the [ERROR] FATAL UNHANDLED EXCEPTION block —
    // the same shape we already parse out of macOS asiBacktraces, with no header
    // or pid. CaptureTime comes from the file's mtime (the only timestamp source).
    internal RhinoCrashReport? TryParseDotNetCrash(string path)
    {
        try
        {
            string text = File.ReadAllText(path);
            var (exception, frames) = ParseManagedExceptionText(text);
            if (exception is null) return null;

            string captureTime = new FileInfo(path).LastWriteTimeUtc
                .ToString("o", CultureInfo.InvariantCulture);
            return new RhinoCrashReport(
                Path: path,
                CaptureTime: captureTime,
                BuildVersion: null,
                Signal: null,
                Termination: null,
                Asi: null,
                ManagedException: exception,
                ManagedFrames: frames,
                TopFrames: Array.Empty<string>());
        }
        catch (Exception ex)
        {
            log.LogDebug("Failed to parse RhinoDotNetCrash.txt at {Path}: {Error}", path, ex.Message);
            return null;
        }
    }

    private RhinoCrashReport? SelectReport(FileInfo[] candidates, int? pid, Func<string, ParsedReport?> parser)
    {
        if (candidates.Length == 0) return null;

        if (pid is int wantPid)
        {
            foreach (var fi in candidates)
            {
                var parsed = parser(fi.FullName);
                if (parsed is null) continue;
                if (parsed.MatchedPid == wantPid) return parsed.Report;
            }
        }

        var cutoff = DateTime.UtcNow - FuzzyMatchWindow;
        foreach (var fi in candidates)
        {
            if (fi.LastWriteTimeUtc < cutoff) break;
            var parsed = parser(fi.FullName);
            if (parsed is null) continue;
            log.LogDebug("Crash report pid match for {Pid} not found; falling back to most recent ({Path})",
                pid?.ToString() ?? "(unspecified)", fi.FullName);
            return parsed.Report;
        }
        return null;
    }

    private void AppendRhinoCrashDumps(List<FileInfo> sink, string rhinoRoot)
    {
        if (!Directory.Exists(rhinoRoot)) return;

        string[] versionDirs;
        try { versionDirs = Directory.GetDirectories(rhinoRoot); }
        catch (Exception ex)
        {
            log.LogDebug("Failed to enumerate Rhino version dirs at {Dir}: {Error}", rhinoRoot, ex.Message);
            return;
        }

        foreach (string vdir in versionDirs)
        {
            foreach (string folder in new[] { "Crash Reports", "CrashDumps", "Crashes" })
            {
                AppendDumpDir(sink, Path.Combine(vdir, folder), "*.dmp");
            }
        }
    }

    private void AppendDumpDir(List<FileInfo> sink, string dir, string pattern)
    {
        if (!Directory.Exists(dir)) return;
        try { sink.AddRange(new DirectoryInfo(dir).GetFiles(pattern)); }
        catch (Exception ex)
        {
            log.LogDebug("Failed to list dumps in {Dir}: {Error}", dir, ex.Message);
        }
    }

    // .ips format: line 1 is a small JSON header, the rest of the file is the
    // full crash-report JSON body. Both are valid JSON in isolation. We use
    // JsonDocument (no source-gen) because we're navigating an external,
    // open-ended schema — typed deserialization would be brittle.
    private ParsedReport? TryParseIps(string path)
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
        if (!root.TryGetProperty("asiBacktraces", out var ab) || ab.ValueKind != JsonValueKind.Array)
            return (null, Array.Empty<string>());

        // Concatenate all entries; in practice there's only one, but be defensive.
        var sb = new System.Text.StringBuilder();
        foreach (var entry in ab.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String) continue;
            sb.AppendLine(entry.GetString());
        }
        return ParseManagedExceptionText(sb.ToString());
    }

    // Shared between the macOS asiBacktraces block and the Windows
    // RhinoDotNetCrash.txt body — both wrap the same `[ERROR] FATAL UNHANDLED
    // EXCEPTION: ... [END ERROR]` shape with `at <Frame> in <path>:line N` rows.
    private static (string? Exception, string[] Frames) ParseManagedExceptionText(string text)
    {
        const int MaxManagedFrames = 10;
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

    // Minidump (.dmp) parser. We avoid dbghelp.dll P/Invoke — symbol resolution
    // needs Rhino's PDBs, which we don't ship. Reading just the header + Exception
    // and MiscInfo streams gets us the agent-actionable bits: pid (to confirm
    // ownership), capture time, and the exception code. Native + managed stacks
    // stay empty on Windows — there's no equivalent of asiBacktraces in a bare
    // minidump.
    private ParsedReport? TryParseMinidump(string path)
    {
        try
        {
            using FileStream fs = File.OpenRead(path);
            using BinaryReader br = new(fs);

            if (fs.Length < 32) return null;
            uint signature = br.ReadUInt32();
            if (signature != MinidumpSignature) return null;
            _ = br.ReadUInt32(); // Version
            uint numStreams = br.ReadUInt32();
            uint streamDirRva = br.ReadUInt32();
            _ = br.ReadUInt32(); // CheckSum
            uint timeDateStamp = br.ReadUInt32();
            _ = br.ReadUInt64(); // Flags

            string captureTime = DateTimeOffset.FromUnixTimeSeconds(timeDateStamp)
                .UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

            int? procId = null;
            uint? exCode = null;
            ulong? exAddr = null;

            if (numStreams > 4096 || streamDirRva + numStreams * 12 > fs.Length) return null;
            fs.Seek(streamDirRva, SeekOrigin.Begin);
            (uint Type, uint Size, uint Rva)[] dirs = new (uint, uint, uint)[numStreams];
            for (int i = 0; i < numStreams; i++)
            {
                dirs[i] = (br.ReadUInt32(), br.ReadUInt32(), br.ReadUInt32());
            }

            foreach (var d in dirs)
            {
                if (d.Type == MinidumpStreamException && d.Size >= 32 && d.Rva + 32 <= fs.Length)
                {
                    fs.Seek(d.Rva, SeekOrigin.Begin);
                    _ = br.ReadUInt32(); // ThreadId
                    _ = br.ReadUInt32(); // __alignment
                    exCode = br.ReadUInt32();
                    _ = br.ReadUInt32(); // ExceptionFlags
                    _ = br.ReadUInt64(); // ExceptionRecord
                    exAddr = br.ReadUInt64();
                }
                else if (d.Type == MinidumpStreamMiscInfo && d.Size >= 12 && d.Rva + 12 <= fs.Length)
                {
                    fs.Seek(d.Rva, SeekOrigin.Begin);
                    _ = br.ReadUInt32(); // SizeOfInfo
                    uint flags1 = br.ReadUInt32();
                    uint pidValue = br.ReadUInt32();
                    if ((flags1 & MiscInfo1ProcessId) != 0) procId = (int)pidValue;
                }
            }

            // WER LocalDumps name dumps "Rhino.exe.<pid>.dmp" — extract from the
            // filename when MiscInfo didn't carry pid.
            procId ??= ExtractPidFromWerName(path);

            string? signal = exCode is uint c ? $"0x{c:X8}" : null;
            string? termination = exCode is uint code ? DescribeExceptionCode(code) : null;
            string? asi = exAddr is ulong a ? $"ExceptionAddress: 0x{a:X16}" : null;

            return new ParsedReport(procId, new RhinoCrashReport(
                Path: path,
                CaptureTime: captureTime,
                BuildVersion: null,
                Signal: signal,
                Termination: termination,
                Asi: asi,
                ManagedException: null,
                ManagedFrames: Array.Empty<string>(),
                TopFrames: Array.Empty<string>()));
        }
        catch (Exception ex)
        {
            log.LogDebug("Failed to parse minidump at {Path}: {Error}", path, ex.Message);
            return null;
        }
    }

    private static int? ExtractPidFromWerName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int lastDot = name.LastIndexOf('.');
        if (lastDot < 0) return null;
        return int.TryParse(name[(lastDot + 1)..], out int pid) ? pid : null;
    }

    private static string DescribeExceptionCode(uint code) => code switch
    {
        0xC0000005 => "EXCEPTION_ACCESS_VIOLATION",
        0xC0000094 => "EXCEPTION_INT_DIVIDE_BY_ZERO",
        0xC00000FD => "EXCEPTION_STACK_OVERFLOW",
        0xC000013A => "STATUS_CONTROL_C_EXIT",
        0xC0000409 => "STATUS_STACK_BUFFER_OVERRUN",
        0xC0000374 => "STATUS_HEAP_CORRUPTION",
        0xE0434352 => "CLR_EXCEPTION",
        0xE06D7363 => "CPP_EXCEPTION",
        _ => $"crashed (code 0x{code:X8})",
    };

    private sealed record ParsedReport(int? MatchedPid, RhinoCrashReport Report);
}
