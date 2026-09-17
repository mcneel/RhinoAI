using System.Threading.Tasks;

using Eto.Forms;

namespace Rhino.AI;

// The GrasshopperAssistant beside the Grasshopper window, inside it.
internal static class GrasshopperRail
{
    private static AssistantRail Rail { get; } = new(
        AIProfile.Grasshopper,
        new GrasshopperRailHost(),
        doc => new GrasshopperAssistantPanel(doc));

    public static bool IsShowing => Rail.IsShowing;

    /// <summary>Puts the assistant beside Grasshopper, opening or revealing it as needed.</summary>
    public static void Toggle(uint documentSerialNumber) => Rail.Toggle(documentSerialNumber);
}

// Grasshopper's editor is a WinForms window — on Windows through .NET's own WinForms, on macOS
// through Rhino's implementation of it — so the rail goes in through NativeRailHost rather than by
// wrapping an Eto Content, which is what the Script Editor's host does.
internal sealed class GrasshopperRailHost : IRailHost
{
    // Below this the canvas stops being usable, so the room the rail gives back stops there.
    private const int MinCanvasWidth = 520;

    public string Name => "Grasshopper";

    public bool IsShowing => GrasshopperWindows.IsShowing;

    private object? Editor { get; set; }
    private NativeRailHost? Native { get; set; }

    public async Task<bool> ShowAsync(uint documentSerialNumber)
    {
        Editor = await GrasshopperWindows.ShowAsync(documentSerialNumber).ConfigureAwait(true);
        return Editor is not null;
    }

    public bool Attach(Control rail, int width)
    {
        if (Editor is not { } editor || !NativeRailHost.TryAttach(editor, rail, width, out NativeRailHost attached))
            return false;

        Native = attached;
        return true;
    }

    public void Detach()
    {
        Native?.Detach();
        Native = null;
    }

    public void ChangeWidthBy(int pixels)
    {
        if (Editor is not { } editor)
            return;

        try
        {
            // WinForms keeps Left when Width changes, so only the right edge moves, as in the editor.
            dynamic window = editor;
            int width = (int)window.Width + pixels;
            window.Width = Math.Max(MinCanvasWidth, width);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[rhino-ai] could not resize the Grasshopper window: {ex.GetBaseException().Message}");
        }
    }
}
