using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RhinoAI;

// Resolves a CLI's launch binary from its search paths and points a ProcessStartInfo at it, shared by
// every agent that spawns a line/ACP CLI so they resolve and launch identically. Two Windows realities
// drive it: a real .exe must beat a .cmd/.bat shim (CreateProcess can't exec a batch file, and cmd.exe
// re-parsing can mangle a newline-bearing arg like Claude's system prompt), yet the shim path is not
// optional because some CLIs ship only a .cmd on Windows (Gemini is node-only).
internal static class CliProcess
{
    // Two passes so a real .exe wins over an npm .cmd shim even when the shim's directory comes first
    // on PATH; within each pass, search-path (PATH-first) order is preserved.
    public static bool TryResolve(IReadOnlyList<string> searchPaths, out string path)
    {
        foreach (string candidate in searchPaths)
            if (!IsBatchShim(candidate) && File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        foreach (string candidate in searchPaths)
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        path = string.Empty;
        return false;
    }

    // Call BEFORE the caller appends any CLI args: a .cmd/.bat on Windows becomes `cmd.exe /c <shim>`,
    // so the `/c <shim>` prefix must lead the ArgumentList that the CLI's own args then follow.
    public static void ConfigureFileName(ProcessStartInfo psi, string path)
    {
        if (OperatingSystem.IsWindows() && IsBatchShim(path))
        {
            psi.FileName = ComSpec();
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(path);
        }
        else
        {
            psi.FileName = path;
        }
    }

    private static bool IsBatchShim(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComSpec() =>
        Environment.GetEnvironmentVariable("COMSPEC")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
}
