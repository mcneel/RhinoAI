using Eto.Drawing;
using Eto.Forms;

namespace RhinoAI.WebPanel;

// The panel's right-click menu, as a real Eto menu rather than HTML.
//
// A webview cannot draw a menu that looks like the rest of Rhino, cannot cascade like a native one,
// and an in-page menu has to fight the panel's own CSS zoom to land under the cursor. Handing the
// gesture to the host costs one command and removes all three problems.
//
// The panel decides what is enabled and what the zoom reads as; this only renders and reports back.
internal static class PanelMenu
{
    public static void Show(Control owner, OpenMenuCommand request, Action<PanelEvent> post, Action reload)
    {
        ContextMenu menu = new();

        if (request.Selection.Trim().Length > 0)
        {
            menu.Items.Add(Item("Copy", Keys.Application | Keys.C, true, () => Clipboard.Instance.Text = request.Selection));
            menu.Items.Add(new SeparatorMenuItem());
        }

        menu.Items.Add(Item("Zoom In", Keys.Application | Keys.Equal, request.CanZoomIn, () => post(new ZoomEvent("in"))));
        menu.Items.Add(Item("Zoom Out", Keys.Application | Keys.Minus, request.CanZoomOut, () => post(new ZoomEvent("out"))));
        // No accelerator: the label already carries the current level.
        menu.Items.Add(Item($"Reset Zoom ({request.ZoomLabel})", Keys.None, request.CanResetZoom, () => post(new ZoomEvent("reset"))));

        menu.Items.Add(new SeparatorMenuItem());
        // No accelerator either: advertising one would claim a chord Rhino may already own, and
        // reload is a recovery action rather than something worth a shortcut.
        menu.Items.Add(Item("Reload", Keys.None, true, reload));

        // The panel sends viewport pixels, which are already this control's coordinates.
        menu.Show(owner, new PointF((float)request.X, (float)request.Y));
    }

    private static ButtonMenuItem Item(string text, Keys shortcut, bool enabled, Action run)
    {
        ButtonMenuItem item = new() { Text = text, Enabled = enabled };
        if (shortcut != Keys.None)
            item.Shortcut = shortcut;
        item.Click += (_, _) => run();
        return item;
    }
}
