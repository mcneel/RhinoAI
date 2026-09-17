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
}
