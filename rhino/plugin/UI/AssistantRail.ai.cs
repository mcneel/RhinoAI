using System.Threading.Tasks;

using Eto.Drawing;
using Eto.Forms;

using Rhino.AI.UI;

namespace Rhino.AI;

// Where a rail can live: the window it belongs beside, and how it gets in and out of it. The Script
// Editor's window is Eto and takes a splitter; Grasshopper's is WinForms and takes a bridge. Nothing
// above this line cares which.
internal interface IRailHost
{
    /// <summary>What to call this window when telling the user it did not open.</summary>
    string Name { get; }

    /// <summary>Its window is on screen. False while it is closed, hidden or never opened.</summary>
    bool IsShowing { get; }

    /// <summary>Opens or reveals the window. False when it did not come up.</summary>
    Task<bool> ShowAsync(uint documentSerialNumber);

    bool Attach(Control rail, int width);

    void Detach();
}

// An assistant as a rail down the right-hand side of the window its work is in, and the window of
// its own it becomes when pulled out of one.
//
// Two buttons of its own, because it has somewhere else to be: the arrow pulls it out into a free
// window and back again, the cross closes it. Everything host-specific is behind IRailHost, so the
// Script Editor and Grasshopper differ in how the rail is attached and in nothing else.
internal sealed class AssistantRail
{
    private const int DefaultWidth = 420;

    public AssistantRail(AIProfile profile, IRailHost host, Func<uint, AIPanel> assistant)
    {
        Profile = profile;
        Host = host;
        Assistant = assistant;
    }

    private AIProfile Profile { get; }
    private IRailHost Host { get; }
    private Func<uint, AIPanel> Assistant { get; }

    private Panel? Rail { get; set; }
    private GlyphButton? Arrow { get; set; }
    private GlyphButton? Cross { get; set; }
    private Form? Floating { get; set; }
    private bool Attached { get; set; }

    // Close() takes down a window whose own Closed handler calls Close(), so it says so.
    private bool Closing { get; set; }

    public bool IsShowing => Rail is { IsDisposed: false };

    public void Toggle(uint documentSerialNumber)
    {
        // Only a rail the user can see is one they are asking to put away. Attached to a window that
        // has since been closed it is not on screen at all, so that reads as "show me the assistant".
        if (IsShowing && (Floating is not null || Host.IsShowing))
        {
            Close();
            return;
        }

        _ = ShowAsync(documentSerialNumber);
    }

    private async Task ShowAsync(uint documentSerialNumber)
    {
        try
        {
            if (!await Host.ShowAsync(documentSerialNumber).ConfigureAwait(true))
            {
                RhinoApp.WriteLine($"[rhino-ai] {Host.Name} did not open, so there is nothing to sit beside.");
                return;
            }

            Build(documentSerialNumber);
            Leave();

            if (Rail is null || !Host.Attach(Rail, DefaultWidth))
            {
                RhinoApp.WriteLine($"[rhino-ai] the assistant could not be put beside {Host.Name}.");
                Close();
                return;
            }

            Attached = true;
            SyncButtons();
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[rhino-ai] could not open the {AIProfiles.Name(Profile)}: {ex.Message}");
        }
    }

    public void Close()
    {
        if (Closing)
            return;

        Closing = true;
        try
        {
            Leave();
            Panel? rail = Rail;
            Rail = null;
            Arrow = null;
            Cross = null;
            // Disposes the assistant with it, so its WebView and its subscriptions go now rather than
            // linger for as long as Rhino runs.
            rail?.Dispose();
        }
        finally
        {
            Closing = false;
        }
    }

    // ------------------------------------------------------------------ the rail

    private void Build(uint documentSerialNumber)
    {
        if (Rail is { IsDisposed: false })
            return;

        // Closing the host window for real disposes everything in it, the rail included. None of
        // that state is worth putting back or safe to touch, so it is forgotten and built again.
        Rail = null;
        Arrow = null;
        Cross = null;
        Floating = null;
        Attached = false;

        Rail = new Panel
        {
            BackgroundColor = Themed(Rhino.ApplicationSettings.PaintColor.PanelBackground),
            Content = new TableLayout
            {
                Rows =
                {
                    new TableRow(Header()),
                    new TableRow(Assistant(documentSerialNumber)) { ScaleHeight = true },
                },
            },
        };
    }

    private Control Header()
    {
        string name = AIProfiles.Name(Profile);

        GlyphButton arrow = new(Mark.Out, ArrowClicked);
        GlyphButton cross = new(Mark.Cross, Close) { ToolTip = $"Close the {name}" };
        Arrow = arrow;
        Cross = cross;
        SyncButtons();

        Label title = new()
        {
            Text = name,
            TextColor = Themed(Rhino.ApplicationSettings.PaintColor.TextDisabled),
            VerticalAlignment = VerticalAlignment.Center,
        };

        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            VerticalContentAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Padding = new Padding(8, 3),
            Items = { new StackLayoutItem(title, expand: true), arrow, cross },
        };
    }

    private void ArrowClicked()
    {
        if (Floating is not null)
        {
            _ = ShowAsync(RhinoDoc.ActiveDoc is { } doc ? doc.RuntimeSerialNumber : 0u);
            return;
        }

        if (Rail is null)
            return;

        Rectangle at = OnScreen();
        Leave();
        EnterFree(at);
    }

    private void SyncButtons()
    {
        bool free = Floating is not null;

        if (Arrow is { } arrow)
        {
            arrow.Glyph = free ? Mark.Back : Mark.Out;
            arrow.ToolTip = free
                ? $"Put the {AIProfiles.Name(Profile)} back beside {Host.Name}"
                : $"Move the {AIProfiles.Name(Profile)} into a window of its own";
            arrow.Invalidate();
        }

        // A free window closes by its own title bar, so a cross of ours beside it would be a second
        // button for the one thing. It is only needed while the rail is inside someone else's window.
        if (Cross is { } cross)
            cross.Visible = !free;
    }

    // ------------------------------------------------------------------ attached, or free

    private void Leave()
    {
        if (Floating is not null)
            LeaveFree();
        else if (Attached)
            Host.Detach();

        Attached = false;
        SyncButtons();
    }

    private void EnterFree(Rectangle at)
    {
        Floating = new Form
        {
            Title = AIProfiles.Name(Profile),
            Size = at.Width > 0 ? at.Size : new Size(DefaultWidth, 720),
            Content = Rail,
            // Owned by Rhino, so a free window stays with the application rather than behind it.
            Owner = Rhino.UI.RhinoEtoApp.MainWindow,
        };

        if (at.Width > 0)
            Floating.Location = at.Location;

        Floating.Closed += OnFloatingClosed;
        SyncButtons();
        Floating.Show();
    }

    private void LeaveFree()
    {
        if (Floating is not { } window)
            return;

        Floating = null;
        window.Closed -= OnFloatingClosed;
        window.Content = null;
        window.Close();
        window.Dispose();
    }

    // Closing the free window is closing the assistant, the same as the cross.
    private void OnFloatingClosed(object? sender, EventArgs e)
    {
        Floating = null;
        Close();
    }

    // Where the rail is now, so the window it becomes opens over the top of it rather than jumping.
    private Rectangle OnScreen()
    {
        try
        {
            if (Rail is { Loaded: true, Size.Width: > 100, Size.Height: > 100 } rail)
                return new Rectangle(Point.Round(rail.PointToScreen(PointF.Empty)), rail.Size);
        }
        catch (Exception)
        {
            // A natively hosted control may not answer this; the window then picks its own size.
        }
        return default;
    }

    private static Color Themed(Rhino.ApplicationSettings.PaintColor which)
    {
        System.Drawing.Color colour = Rhino.ApplicationSettings.AppearanceSettings.GetPaintColor(which);
        return Color.FromArgb(colour.R, colour.G, colour.B);
    }

    // ------------------------------------------------------------------ the two buttons

    private enum Mark { Cross, Out, Back }

    // Drawn rather than loaded: a cross and an arrow are a few lines each, drawing them takes Rhino's
    // own text colour so they follow the theme, and they stay crisp at every scale.
    private sealed class GlyphButton : Drawable
    {
        public GlyphButton(Mark mark, Action clicked)
        {
            Glyph = mark;
            Size = new Size(20, 20);
            Cursor = Cursors.Pointer;
            MouseEnter += (_, _) => { Hot = true; Invalidate(); };
            MouseLeave += (_, _) => { Hot = false; Invalidate(); };
            MouseUp += (_, _) => clicked();
        }

        public Mark Glyph { get; set; }

        private bool Hot { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.AntiAlias = true;

            float w = Size.Width;
            float h = Size.Height;
            Color ink = Themed(Rhino.ApplicationSettings.PaintColor.TextEnabled);

            if (Hot)
                g.FillRectangle(Color.FromArgb(ink.Rb, ink.Gb, ink.Bb, 38), new RectangleF(0, 0, w, h));

            float inset = w * 0.3f;
            float mid = h / 2f;
            float head = w * 0.2f;
            using Pen pen = new(ink, 1.3f);

            switch (Glyph)
            {
                case Mark.Cross:
                    g.DrawLine(pen, inset, inset, w - inset, h - inset);
                    g.DrawLine(pen, w - inset, inset, inset, h - inset);
                    break;

                case Mark.Out:
                    g.DrawLine(pen, inset, mid, w - inset, mid);
                    g.DrawLine(pen, w - inset - head, mid - head, w - inset, mid);
                    g.DrawLine(pen, w - inset - head, mid + head, w - inset, mid);
                    break;

                case Mark.Back:
                    g.DrawLine(pen, inset, mid, w - inset, mid);
                    g.DrawLine(pen, inset + head, mid - head, inset, mid);
                    g.DrawLine(pen, inset + head, mid + head, inset, mid);
                    break;
            }
        }
    }
}
