using System.Runtime.InteropServices;

namespace Rhino.AI.Router;

// Whether a process still owns a visible top-level window, i.e. whether a human
// could close it by hand. CloseAsync uses this to decide whether an adopted slot
// (one the router did not spawn) can be closed cooperatively: a hidden-window
// slot that outlived its router has no window for anyone to close, so it must
// not be refused the same way a normal adopted Rhino is.
public enum WindowVisibility { Visible, Hidden, Unknown }

public interface IWindowProbe
{
    WindowVisibility Probe(int pid);
}

// Windows: EnumWindows every top-level window on the desktop, keep the ones
// owned by pid, and check IsWindowVisible. Same technique used to verify
// --hidden live against a real Rhino (see the hidden-window handoff): a hidden
// Rhino still creates its usual ~20 top-level windows, just none visible, so
// "no window owned by pid was visible" is Hidden, not "no window exists".
//
// Non-Windows: no equivalent API here, so this always reports Unknown. CloseAsync
// treats Unknown the same as Visible (refuse), which is what kept the pre-existing
// behavior on macOS -- this class changes nothing there.
internal sealed class WindowProbe : IWindowProbe
{
    public WindowVisibility Probe(int pid)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowVisibility.Unknown;

        try
        {
            bool visible = false;
            bool sawAny = false;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid != (uint)pid) return true; // keep enumerating

                sawAny = true;
                if (IsWindowVisible(hWnd))
                {
                    visible = true;
                    return false; // found one; stop early
                }
                return true;
            }, IntPtr.Zero);

            if (visible) return WindowVisibility.Visible;
            // Rhino always creates its main frame's top-level windows, visible or
            // not, so seeing none at all for a live pid is as inconclusive as a
            // platform with no probe -- refuse rather than guess.
            return sawAny ? WindowVisibility.Hidden : WindowVisibility.Unknown;
        }
        catch
        {
            return WindowVisibility.Unknown;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}
