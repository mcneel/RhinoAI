using System.IO;
using System.Runtime.InteropServices;

namespace RhMcp.Internal;

/// <summary>
/// Captures the currently-foreground window (typically a modal dialog) as PNG.
///
/// Eto-level abstraction: callers stay platform-agnostic and get back PNG bytes
/// (or an Eto.Drawing.Bitmap via <see cref="CaptureTopmostAsEto"/>). Finding the
/// foreground window is necessarily OS-specific — Eto's window list only knows
/// about windows Eto owns, so native Rhino/OS dialogs need a platform lookup.
/// </summary>
internal static class DialogScreenshot
{
    public static byte[]? CaptureTopmost()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsImpl.Capture();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return MacImpl.Capture();
        return null;
    }

    public static Eto.Drawing.Bitmap? CaptureTopmostAsEto()
    {
        var png = CaptureTopmost();
        if (png is null) return null;
        using var ms = new MemoryStream(png);
        return new Eto.Drawing.Bitmap(ms);
    }

    // --- Windows -----------------------------------------------------------

    private static class WindowsImpl
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // PW_RENDERFULLCONTENT: required for DWM-composited windows (most modern
        // dialogs). Without it PrintWindow returns a black bitmap for anything
        // using hardware acceleration.
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public static byte[]? Capture()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            if (!GetWindowRect(hwnd, out var rect)) return null;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0) return null;

            using var bmp = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                try { PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
                finally { g.ReleaseHdc(hdc); }
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // --- macOS -------------------------------------------------------------

    private static class MacImpl
    {
        private const string CoreGraphics =
            "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string ImageIO =
            "/System/Library/Frameworks/ImageIO.framework/ImageIO";

        // CGWindowListOption
        private const uint kCGWindowListOptionOnScreenOnly = 1 << 0;
        private const uint kCGWindowListExcludeDesktopElements = 1 << 4;
        private const uint kCGWindowListOptionIncludingWindow = 1 << 3;
        // CGWindowImageOption
        private const uint kCGWindowImageBoundsIgnoreFraming = 1 << 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct CGRect { public double X, Y, W, H; }
        private static readonly CGRect CGRectNull = new() { X = double.PositiveInfinity };

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGWindowListCreateImage(
            CGRect screenBounds, uint listOption, uint windowID, uint imageOption);

        [DllImport(CoreFoundation)]
        private static extern long CFArrayGetCount(IntPtr theArray);
        [DllImport(CoreFoundation)]
        private static extern IntPtr CFArrayGetValueAtIndex(IntPtr theArray, long idx);
        [DllImport(CoreFoundation)]
        private static extern IntPtr CFDictionaryGetValue(IntPtr theDict, IntPtr key);
        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr cf);
        [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
        private static extern IntPtr CFStringCreateWithCharacters(IntPtr alloc, string s, long len);
        [DllImport(CoreFoundation)]
        private static extern bool CFNumberGetValue(IntPtr num, long type, out uint value);
        private const long kCFNumberSInt32Type = 3;

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFDataCreateMutable(IntPtr alloc, long capacity);
        [DllImport(CoreFoundation)]
        private static extern long CFDataGetLength(IntPtr data);
        [DllImport(CoreFoundation)]
        private static extern IntPtr CFDataGetBytePtr(IntPtr data);

        [DllImport(ImageIO)]
        private static extern IntPtr CGImageDestinationCreateWithData(
            IntPtr data, IntPtr type, long count, IntPtr options);
        [DllImport(ImageIO)]
        private static extern void CGImageDestinationAddImage(
            IntPtr dest, IntPtr image, IntPtr properties);
        [DllImport(ImageIO)]
        private static extern bool CGImageDestinationFinalize(IntPtr dest);

        [DllImport(CoreGraphics)]
        private static extern void CGImageRelease(IntPtr image);

        public static byte[]? Capture()
        {
            // Topmost on-screen window, skipping desktop chrome (menubar, dock).
            var list = CGWindowListCopyWindowInfo(
                kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, 0);
            if (list == IntPtr.Zero) return null;

            try
            {
                if (CFArrayGetCount(list) == 0) return null;
                var frontDict = CFArrayGetValueAtIndex(list, 0);

                var key = CFStringCreateWithCharacters(IntPtr.Zero, "kCGWindowNumber", 15);
                var numRef = CFDictionaryGetValue(frontDict, key);
                CFRelease(key);
                if (numRef == IntPtr.Zero) return null;
                if (!CFNumberGetValue(numRef, kCFNumberSInt32Type, out var windowId)) return null;

                var image = CGWindowListCreateImage(
                    CGRectNull, kCGWindowListOptionIncludingWindow, windowId,
                    kCGWindowImageBoundsIgnoreFraming);
                if (image == IntPtr.Zero) return null;

                try { return EncodePng(image); }
                finally { CGImageRelease(image); }
            }
            finally { CFRelease(list); }
        }

        private static byte[]? EncodePng(IntPtr cgImage)
        {
            var data = CFDataCreateMutable(IntPtr.Zero, 0);
            if (data == IntPtr.Zero) return null;
            try
            {
                var pngType = CFStringCreateWithCharacters(IntPtr.Zero, "public.png", 10);
                var dest = CGImageDestinationCreateWithData(data, pngType, 1, IntPtr.Zero);
                CFRelease(pngType);
                if (dest == IntPtr.Zero) return null;

                try
                {
                    CGImageDestinationAddImage(dest, cgImage, IntPtr.Zero);
                    if (!CGImageDestinationFinalize(dest)) return null;
                }
                finally { CFRelease(dest); }

                var len = CFDataGetLength(data);
                var ptr = CFDataGetBytePtr(data);
                var bytes = new byte[len];
                Marshal.Copy(ptr, bytes, 0, (int)len);
                return bytes;
            }
            finally { CFRelease(data); }
        }
    }
}
