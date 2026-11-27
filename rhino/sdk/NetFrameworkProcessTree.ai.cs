#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace Rhino.AI;

internal static class ProcessTree
{
    private const int ProcessTerminate = 0x0001;
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int Synchronize = 0x00100000;
    private const int Access = ProcessTerminate | ProcessQueryLimitedInformation | Synchronize;
    private const int SnapProcess = 0x00000002;
    private const int WaitObject0 = 0;

    public static void Kill(int rootId)
    {
        using SafeProcessHandle root = OpenProcess(Access, false, rootId);
        if (root.IsInvalid)
            throw new InvalidOperationException($"Process {rootId} has exited.");

        List<Exception> failures = [];
        TerminateThenDescend(rootId, root, failures);
        if (failures.Count > 0)
            throw new AggregateException("Not all processes in the tree could be terminated.", failures);
    }

    private static void TerminateThenDescend(int id, SafeProcessHandle process, List<Exception> failures)
    {
        if (!TerminateProcess(process, -1))
        {
            int error = Marshal.GetLastWin32Error();
            if (WaitForSingleObject(process, 0) != WaitObject0)
                failures.Add(new Win32Exception(error));
        }

        if (!GetProcessTimes(process, out long started, out _, out _, out _))
        {
            failures.Add(new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }

        foreach (int childId in ChildIds(id))
        {
            using SafeProcessHandle child = OpenProcess(Access, false, childId);
            if (child.IsInvalid)
                continue;

            // A child older than its parent is an orphan whose dead parent's PID was reused by this process.
            if (!GetProcessTimes(child, out long childStarted, out _, out _, out _) || childStarted < started)
                continue;

            TerminateThenDescend(childId, child, failures);
        }
    }

    private static List<int> ChildIds(int parentId)
    {
        using SafeFileHandle snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot.IsInvalid)
            throw new Win32Exception();

        List<int> children = [];
        ProcessEntry32 entry = new() { Size = Marshal.SizeOf<ProcessEntry32>() };
        for (bool more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
        {
            if (entry.ParentProcessId == parentId && entry.ProcessId != parentId)
                children.Add(entry.ProcessId);
        }

        return children;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public int Size;
        public int Usage;
        public int ProcessId;
        public IntPtr DefaultHeapId;
        public int ModuleId;
        public int Threads;
        public int ParentProcessId;
        public int PriorityClassBase;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle process, int exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int WaitForSingleObject(SafeProcessHandle handle, int milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(int flags, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry32 entry);
}
#endif
