using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RhMcp.Router;

// CreateProcess + CREATE_BREAKAWAY_FROM_JOB lets the child escape any Job Object
// the router inherited (e.g. VS Code extension host). Without breakaway, GUI
// children come up alive but can't paint a main window.
//
// Process.Start + UseShellExecute=true would also fix the window problem, but
// ShellExecute silently ignores psi.Environment, which would race concurrent
// spawns on RHINO_MCP_AUTOSTART_PORT.
internal static class WinSpawn
{
    private const uint CREATE_BREAKAWAY_FROM_JOB  = 0x01000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    public static Process Start(string exePath, string arguments, IDictionary<string, string> extraEnv)
    {
        // CreateProcess can write into lpCommandLine; pre-size to Windows' 32k limit.
        StringBuilder cmdLine = new (32768);
        cmdLine.Append('"').Append(exePath).Append('"').Append(' ').Append(arguments);

        var startup = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>() };
        IntPtr envBlock = BuildEnvBlock(extraEnv);
        try
        {
            if (!CreateProcessW(
                lpApplicationName: null,
                lpCommandLine: cmdLine,
                lpProcessAttributes: IntPtr.Zero,
                lpThreadAttributes: IntPtr.Zero,
                bInheritHandles: false,
                dwCreationFlags: CREATE_BREAKAWAY_FROM_JOB | CREATE_UNICODE_ENVIRONMENT,
                lpEnvironment: envBlock,
                lpCurrentDirectory: null,
                lpStartupInfo: ref startup,
                lpProcessInformation: out PROCESS_INFORMATION pi))
            {
                int err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err,
                    $"CreateProcess failed for '{exePath}' (error {err}). " +
                    $"If access-denied, the router's parent Job Object disallows breakaway.");
            }

            CloseHandle(pi.hThread);
            try { return Process.GetProcessById((int)pi.dwProcessId); }
            finally { CloseHandle(pi.hProcess); }
        }
        finally
        {
            Marshal.FreeHGlobal(envBlock);
        }
    }

    // UTF-16 env block (KEY=val\0...\0\0), case-insensitive ordinal sort required by Windows.
    private static IntPtr BuildEnvBlock(IDictionary<string, string> overrides)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
            merged[(string)e.Key] = (string)(e.Value ?? "");

        // Empty windir (Claude Desktop's MSIX env) crashes Rhino in WPF font-cache init; backfill from
        // SystemDirectory, which (unlike GetFolderPath(Windows)) survives a process born with windir unset.
        string winDir = Path.GetDirectoryName(Environment.SystemDirectory) ?? "";
        if (!string.IsNullOrEmpty(winDir))
        {
            if (!merged.TryGetValue("windir", out var w) || string.IsNullOrEmpty(w)) merged["windir"] = winDir;
            if (!merged.TryGetValue("SystemRoot", out var s) || string.IsNullOrEmpty(s)) merged["SystemRoot"] = winDir;
        }

        foreach (var kv in overrides)
            merged[kv.Key] = kv.Value;

        var sb = new StringBuilder();
        foreach (var kv in merged)
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
        sb.Append('\0');
        return Marshal.StringToHGlobalUni(sb.ToString());
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOW
    {
        public uint cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
