using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RhMcp.Router;

// Launches a Windows child via CreateProcess with CREATE_BREAKAWAY_FROM_JOB so
// it escapes any Job Object the router inherited from its parent (e.g. the
// Claude Code / VS Code extension host that spawned node -> the router).
// Without breakaway, GUI children can come up alive but with no interactive
// desktop and never paint a main window — see RhinoManager.LaunchAsLeaderAsync's
// "never created a main window" diagnostic.
//
// We don't use Process.Start with UseShellExecute=true (which would also fix
// the window problem) because ShellExecute silently ignores psi.Environment.
// Going through CreateProcess keeps the env block per-child, so concurrent
// spawns don't race on RHINO_MCP_AUTOSTART_PORT.
internal static class WinSpawn
{
    private const uint CREATE_BREAKAWAY_FROM_JOB  = 0x01000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    public static Process Start(string exePath, string arguments, IDictionary<string, string> extraEnv)
    {
        // CreateProcess can write into lpCommandLine; hand it a pre-sized buffer.
        // 32k matches Windows' command-line limit.
        var cmdLine = new StringBuilder(32768);
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

    // UTF-16 env block: KEY1=val1\0KEY2=val2\0\0. Windows env-var lookup is
    // case-insensitive, and the block must be sorted in case-insensitive
    // ordinal order or some apps misbehave.
    private static IntPtr BuildEnvBlock(IDictionary<string, string> overrides)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
            merged[(string)e.Key] = (string)(e.Value ?? "");
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
