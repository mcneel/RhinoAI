using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RhMcp;

internal enum ScriptRail
{
    FileAccess,
    FileWrite,
    Network,
    ProcessLaunch,
    EnvironmentRead,
}

// Rhino-free so the test project can compile it in. Best-effort by design: it steers a cooperative
// agent by naming exactly what was matched; it is not a sandbox.
internal static class ScriptGuard
{
    public static IReadOnlyList<string> ScanScript(ScriptRail rail, string source)
    {
        if (string.IsNullOrEmpty(source))
            return [];
        return ScriptPatterns[rail]
            .Where(p => p.Pattern.IsMatch(source))
            .Select(p => p.Label)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> ScanCommand(ScriptRail rail, string command)
    {
        Regex? regex = rail switch
        {
            ScriptRail.FileWrite => FileWriteCommands,
            ScriptRail.FileAccess => FileAccessCommands,
            _ => null,
        };
        return regex is null ? [] : MatchedNames(regex, command);
    }

    // Without this, run_command("_-RunPythonScript ...") is a one-line bypass of every script scan.
    public static IReadOnlyList<string> ScanCommandForScriptEscapes(string command) =>
        MatchedNames(ScriptEscapeCommands, command);

    private static IReadOnlyList<string> MatchedNames(Regex regex, string command)
    {
        if (string.IsNullOrEmpty(command))
            return [];
        return regex.Matches(command)
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static (string Label, Regex Pattern)[] Tokens(params string[] tokens) =>
        tokens.Select(t => (t, TokenRegex(t))).ToArray();

    private static Regex TokenRegex(string token) =>
        new((char.IsLetter(token[0]) ? @"\b" : string.Empty) + Regex.Escape(token), RegexOptions.CultureInvariant);

    // Longest names first: alternation is ordered, so "saveas" must win over "save".
    private static Regex CommandRegex(params string[] names) =>
        new($@"(?i)(?<![A-Za-z0-9])(?:{string.Join("|", names)})(?![A-Za-z])", RegexOptions.CultureInvariant);

    private static (string Label, Regex Pattern)[] FileWritePatterns { get; } =
    [
        .. Tokens(
            "File.Delete", "File.Move", "File.Copy", "File.Create", "File.Replace", "File.WriteAll",
            "File.AppendAll", "File.OpenWrite", "Directory.Delete", "Directory.Move", "Directory.CreateDirectory",
            "FileStream", "StreamWriter", "BinaryWriter", "ZipFile", "WriteFile", "ExportSelected",
            "os.remove", "os.unlink", "os.rename", "os.replace", "os.rmdir", "os.removedirs",
            "os.mkdir", "os.makedirs", "shutil.", "tempfile.",
            ".write_text(", ".write_bytes(", ".unlink(", ".rmdir("),
        ("open( with a write mode", new Regex(@"\bopen\s*\([^)]*['""][rbt+]*[wax+]", RegexOptions.CultureInvariant)),
    ];

    private static (string Label, Regex Pattern)[] FileAccessPatterns { get; } =
    [
        .. FileWritePatterns,
        .. Tokens(
            "File.ReadAll", "File.OpenRead", "File.OpenText", "File.Open", "File.Exists",
            "StreamReader", "Directory.GetFiles", "Directory.EnumerateFiles", "Directory.GetDirectories",
            "Directory.Exists", "System.IO",
            "os.listdir", "os.walk", "os.scandir", "os.path.", "pathlib", "io.open", "codecs.open",
            ".read_text(", ".read_bytes("),
        ("open(", new Regex(@"\bopen\s*\(", RegexOptions.CultureInvariant)),
    ];

    private static (string Label, Regex Pattern)[] NetworkPatterns { get; } =
        Tokens(
            "HttpClient", "WebClient", "HttpWebRequest", "WebRequest", "TcpClient", "UdpClient",
            "Socket", "Dns.", "System.Net",
            "urllib", "requests.", "import requests", "from requests", "http.client", "httpx",
            "aiohttp", "socket.", "ftplib", "smtplib", "websocket", "urlopen(");

    private static (string Label, Regex Pattern)[] ProcessPatterns { get; } =
        Tokens(
            "Process.Start", "ProcessStartInfo", "System.Diagnostics.Process",
            "subprocess", "os.system", "os.popen", "os.exec", "os.spawn", "os.startfile", "Popen(");

    private static (string Label, Regex Pattern)[] EnvironmentPatterns { get; } =
        Tokens(
            "Environment.GetEnvironmentVariable", "Environment.ExpandEnvironmentVariables",
            "os.environ", "os.getenv");

    private static IReadOnlyDictionary<ScriptRail, (string Label, Regex Pattern)[]> ScriptPatterns { get; } =
        new Dictionary<ScriptRail, (string, Regex)[]>
        {
            [ScriptRail.FileAccess] = FileAccessPatterns,
            [ScriptRail.FileWrite] = FileWritePatterns,
            [ScriptRail.Network] = NetworkPatterns,
            [ScriptRail.ProcessLaunch] = ProcessPatterns,
            [ScriptRail.EnvironmentRead] = EnvironmentPatterns,
        };

    private static Regex FileWriteCommands { get; } = CommandRegex(
        "saveastemplate", "incrementalsave", "savesmall", "saveas", "save",
        "exportselected", "export", "viewcapturetofile", "screencapturetofile");

    private static Regex FileAccessCommands { get; } = CommandRegex(
        "saveastemplate", "incrementalsave", "savesmall", "saveas", "save",
        "exportselected", "export", "viewcapturetofile", "screencapturetofile",
        "readcommandfile", "import", "insert", "revert", "open");

    private static Regex ScriptEscapeCommands { get; } = CommandRegex(
        "runpythonscript", "editpythonscript", "readcommandfile", "runscript", "scripteditor");
}
