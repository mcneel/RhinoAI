using System.Threading.Tasks;

using Gh1Instances = Grasshopper.Instances;

namespace Rhino.AI;

// Finding the Grasshopper window, and opening it when it is not up yet. It is one window for the
// whole application, which is why there is one assistant that goes to it rather than one per canvas.
//
// It is a WinForms window, so it comes back as an object, to be driven through NativeRailHost.
internal static class GrasshopperWindows
{
    private const int PollMs = 100;

    // Grasshopper's first open loads every component library, which on a cold start is not quick.
    private const int PollTries = 600;

    // Through IsDocumentEditor first: reading DocumentEditor is not a question, and asking it before
    // Grasshopper has an editor is not the same as asking whether it has one.
    private static object? Window => Gh1Instances.IsDocumentEditor ? Gh1Instances.DocumentEditor : null;

    public static bool IsShowing => Window is { } editor && IsVisible(editor);

    /// <summary>The Grasshopper editor window, on screen. Null when it did not come up.</summary>
    public static async Task<object?> ShowAsync(uint documentSerialNumber)
    {
        if (Window is { } open && IsVisible(open))
            return Front(open);

        // The command, so Grasshopper opens the way it always does — its own splash, its own account
        // of itself on the command line — and so a window that is merely closed is shown again, which
        // is what Grasshopper does with it rather than destroying it.
        RhinoApp.RunScript(documentSerialNumber, "_Grasshopper", echo: false);

        for (int i = 0; i < PollTries; i++)
        {
            await Task.Delay(PollMs).ConfigureAwait(true);
            if (Window is { } opened && IsVisible(opened))
                return Front(opened);
        }
        return null;
    }

    private static bool IsVisible(object window)
    {
        try
        {
            return (bool)((dynamic)window).Visible;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static object Front(object window)
    {
        try
        {
            ((dynamic)window).BringToFront();
        }
        catch (Exception)
        {
            // Being in front is a courtesy; the rail does not depend on it.
        }
        return window;
    }
}
