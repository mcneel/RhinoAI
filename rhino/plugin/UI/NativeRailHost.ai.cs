using System.IO;
using System.Reflection;

using Eto.Forms;

namespace Rhino.AI;

// Putting an Eto control inside a WinForms window, which is what Grasshopper 1's editor is — on
// Windows through .NET's own WinForms, on macOS through Rhino's implementation of it.
//
// Nothing here is compiled against WinForms: the plug-in is one assembly for both platforms and a
// WinForms reference would make it Windows-only. So the host window is driven as `dynamic`, and the
// two types this needs — Control and DockStyle — are read off the window itself rather than named.
//
// The platforms hand a control over differently, and that is the only branch in here: a WPF element
// goes into an ElementHost, an NSView is wrapped by WinForms' own Control.FromHandle. Both are how
// the frameworks are meant to be mixed, and Grasshopper already hosts WPF in its own window on
// Windows, so the arrangement is not a novel one for it.
internal sealed class NativeRailHost
{
    private NativeRailHost(object window, object host, Control content, bool owned)
    {
        Window = window;
        Host = host;
        Content = content;
        Owned = owned;
    }

    private object Window { get; }

    // The WinForms control now sitting in the window: an ElementHost we made, or the wrapper WinForms
    // made around our NSView.
    private object Host { get; }

    private Control Content { get; }

    // Only the ElementHost is ours to dispose. The Mac wrapper is a shell around a view Eto still
    // owns and expects to be handed back.
    private bool Owned { get; }

    public static bool TryAttach(object window, Control content, int width, out NativeRailHost attached)
    {
        attached = default!;
        if (Bridge is null)
            return false;

        object? native;
        try
        {
            native = Bridge.Invoke(null, [content, true]);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[rhino-ai] Eto would not hand the assistant over: {ex.GetBaseException().Message}");
            return false;
        }

        if (native is null)
            return false;

        Stretch(content);

        if (Wrap(window, native) is not { } host)
        {
            content.DetachNative();
            return false;
        }

        try
        {
            dynamic form = window;
            dynamic child = host;

            // Through a dynamic local, not straight from Enum.Parse: a dynamic call binds its
            // arguments by their STATIC type, and object does not convert to DockStyle.
            dynamic right = Enum.Parse(Docks(window), "Right");
            child.Dock = right;
            child.Width = width;

            // Added last, and left there. WinForms docks its children from the LAST index to the
            // first, so the newest one docks first and takes the full height of the window before
            // the menu bar and the status bar claim their bands — which is the rail running from
            // the top, beside everything, rather than a strip between them.
            form.Controls.Add(child);

            form.PerformLayout();
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[rhino-ai] could not put the assistant in the window: {ex.GetBaseException().Message}");
            content.DetachNative();
            return false;
        }

        attached = new NativeRailHost(window, host, content, Eto.Platform.Instance.IsWpf);
        return true;
    }

    public void Detach()
    {
        try
        {
            dynamic form = Window;
            dynamic child = Host;
            form.Controls.Remove(child);

            if (Owned)
            {
                // Emptied first: disposing a host still holding the element would take the element
                // with it, and the rail is about to be used somewhere else.
                child.Child = null;
                child.Dispose();
            }

            form.PerformLayout();
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[rhino-ai] could not take the assistant out of the window: {ex.GetBaseException().Message}");
        }

        Content.DetachNative();
    }

    private static object? Wrap(object window, object native)
    {
        if (Eto.Platform.Instance.IsMac)
        {
            if (Controls(window)?.GetMethod("FromHandle", BindingFlags.Public | BindingFlags.Static, [typeof(IntPtr)])
                is not { } fromHandle)
                return null;

            // NSView.Handle is a plain pointer on some bindings and a NativeHandle on others; the cast
            // resolves either, which is the whole reason this goes through dynamic.
            dynamic view = native;
            IntPtr handle = (IntPtr)view.Handle;
            return fromHandle.Invoke(null, [handle]);
        }

        if (ElementHosts is not { } host)
            return null;

        // Filling the host is the host's decision, so the element arrives with nothing of its own to
        // say about its size. Eto's hand-over pins both, and an ElementHost honours what it is given.
        Type shape = native.GetType();
        dynamic element = native;
        element.Width = double.NaN;
        element.Height = double.NaN;
        Fill(shape, element, "HorizontalAlignment");
        Fill(shape, element, "VerticalAlignment");

        // Same reason as the Dock assignment: as a statically-typed object this does not convert to
        // the UIElement the property takes, however WPF-shaped the thing actually is.
        dynamic wrapper = Activator.CreateInstance(host)!;
        wrapper.Child = element;
        return wrapper;
    }

    private static void Fill(Type shape, dynamic element, string property)
    {
        if (shape.GetProperty(property) is not { } found)
            return;

        dynamic stretch = Enum.Parse(found.PropertyType, "Stretch");
        found.SetValue(element, stretch);
    }

    // Eto's hand-over turns the control's own stretching off — WpfHelpers.ToNative ends in
    // SetScale(false, false) — which pins the control to its preferred size, leaving a small panel
    // adrift in a large host. Here the host is what sizes it, so stretching goes back on: the same
    // call Eto itself makes for the controls it hosts inside native ones.
    private static void Stretch(Control content)
    {
        try
        {
            object handler = content.Handler;
            handler?.GetType().GetMethod("SetScale", [typeof(bool), typeof(bool)])?.Invoke(handler, [true, true]);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[rhino-ai] the assistant may not fill its side of the window: {ex.GetBaseException().Message}");
        }
    }

    // DockStyle, taken from the window's own Dock property rather than named.
    private static Type Docks(object window) => window.GetType().GetProperty("Dock")!.PropertyType;

    // System.Windows.Forms.Control, found by walking the window's own base types: it is the one type
    // in this file that has to be named, and this way it is never loaded from the wrong assembly.
    private static Type? Controls(object window)
    {
        for (Type? type = window.GetType(); type is not null; type = type.BaseType)
        {
            if (type.FullName == "System.Windows.Forms.Control")
                return type;
        }
        return null;
    }

    private static Type? Hosts { get; set; }

    private static Type? ElementHosts =>
        Hosts ??= Type.GetType("System.Windows.Forms.Integration.ElementHost, WindowsFormsIntegration", throwOnError: false)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("System.Windows.Forms.Integration.ElementHost", throwOnError: false))
                .FirstOrDefault(type => type is not null);

    private static MethodInfo? Handover { get; set; }

    private static MethodInfo? Bridge => Handover ??= FindBridge();

    // Eto's own hand-over to a native application: WpfHelpers.ToNative on Windows, and on macOS
    // whichever of MacOSHelpers / MonoMac64Helpers / MonoMacHelpers this Eto was built as. Found by
    // shape, so the flavour never has to be guessed, and it takes attach: true, which is what fires
    // the control's load events in the absence of an Eto parent to do it.
    private static MethodInfo? FindBridge()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name is not { } name || !name.StartsWith("Eto.", StringComparison.Ordinal))
                continue;

            try
            {
                foreach (Type type in assembly.GetExportedTypes())
                {
                    if (type.Namespace != "Eto.Forms" || !type.IsAbstract || !type.IsSealed)
                        continue;

                    if (type.GetMethod("ToNative", BindingFlags.Public | BindingFlags.Static, [typeof(Control), typeof(bool)])
                        is { } found)
                        return found;
                }
            }
            catch (Exception ex) when (ex is ReflectionTypeLoadException or FileNotFoundException or TypeLoadException)
            {
            }
        }

        RhinoApp.WriteLine("[rhino-ai] this Eto has no native hand-over, so the assistant cannot go inside the window.");
        return null;
    }
}
