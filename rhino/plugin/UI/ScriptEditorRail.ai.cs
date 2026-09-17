#if R9

using System.Threading.Tasks;

using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI;

// The ScriptAssistant beside Rhino's Script Editor, inside the editor's own window.
internal static class ScriptEditorRail
{
    private static AssistantRail Rail { get; } = new(
        AIProfile.Script,
        new ScriptEditorRailHost(),
        doc => new ScriptAssistantPanel(doc));

    public static bool IsShowing => Rail.IsShowing;

    /// <summary>Puts the assistant beside the editor, opening or revealing the editor as needed.</summary>
    public static void Toggle(uint documentSerialNumber) => Rail.Toggle(documentSerialNumber);
}

// The editor cannot host a Rhino panel — it is a separate Eto window owned by the RhinoCode plug-in,
// with no docking seam and no reference to Rhino.UI.Panels — but it is Eto all the way down, and its
// Content is an ordinary control. So the rail is made by wrapping that Content in a splitter and
// putting the assistant in the other half. Deliberately wrapping Content rather than reaching into
// the editor's own layout: its body is built by a protected virtual method out of the editor's own
// types, and we would be guessing at a shape that is free to change. Content is just a property.
//
// Nothing is added to the editor's own toolbar. The rail carries the two buttons that matter — put
// me in a window of my own, close me — where the rail is, and a third button in someone else's
// dashboard saying the same thing was one too many.
internal sealed class ScriptEditorRailHost : IRailHost
{
    // Below this the code half stops being usable, so the rail gives up its preferred width first.
    private const int MinEditorWidth = 520;

    // Below this the assistant stops being usable, so the splitter will not drag it narrower.
    private const int MinRailWidth = 260;

    public string Name => "the Script Editor";

    public bool IsShowing => ScriptEditorSession.IsShowing;

    private Window? Editor { get; set; }
    private Splitter? Split { get; set; }

    public async Task<bool> ShowAsync(uint documentSerialNumber)
    {
        Editor = await ScriptEditorSession.ShowWindowAsync().ConfigureAwait(true);
        return Editor is not null;
    }

    public bool Attach(Control rail, int width)
    {
        if (Editor is not { IsDisposed: false } editor || editor.Content is not { } existing)
            return false;

        Split = new Splitter
        {
            Orientation = Orientation.Horizontal,
            // The rail keeps its width when the editor is resized; the code half takes the change.
            FixedPanel = SplitterFixedPanel.Panel2,
            Panel1 = existing,
            Panel2 = rail,
            // Neither half can be dragged away to nothing.
            Panel1MinimumSize = MinEditorWidth,
            Panel2MinimumSize = MinRailWidth,
            // Panel2's own width under this FixedPanel, applied at the first real layout rather than now.
            RelativePosition = width,
        };

        editor.Content = Split;
        return true;
    }

    public void ChangeWidthBy(int pixels)
    {
        if (Editor is not { IsDisposed: false } editor)
            return;

        int width = Math.Max(MinEditorWidth, editor.Width + pixels);

        // Assigned as a Size so only the right edge moves: that is the side the rail was on.
        editor.Size = new Size(width, editor.Height);
    }

    public void Detach()
    {
        if (Split is not { } split)
            return;

        Split = null;

        // Detached first: a control cannot be in two parents, and assigning the body back to Content
        // while the splitter still holds it is exactly that.
        Control? body = split.Panel1;
        split.Panel1 = null;
        split.Panel2 = null;

        if (Editor is { IsDisposed: false } editor && body is not null)
            editor.Content = body;

        split.Dispose();
    }
}

#endif
