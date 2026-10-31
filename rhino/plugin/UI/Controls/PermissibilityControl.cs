using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI;

internal sealed class PermissibilityControl : Drawable
{

    private const float TrackInset = 2f;
    private const float SlideStep = 0.35f;

    private static Color DenyColour { get; } = Color.FromArgb(0xE5, 0x48, 0x4D);
    private static Color AskColour { get; } = Color.FromArgb(0xF5, 0xA5, 0x24);
    private static Color AlwaysColour { get; } = Color.FromArgb(0x30, 0xA4, 0x6C);
    private static Color UnsetColour { get; } = Color.FromArgb(0x8E, 0x8E, 0x93);

    private Permissability state = Permissability.Unset;

    // Eto's PropertyBinding listens for "<Property>Changed", which makes BindDataContext two-way.
    public event EventHandler? StateChanged;

    public Permissability State
    {
        get => state;
        set
        {
            if (state == value) return;
            state = value;
            if (Slot(value) is float target)
            {
                if (KnobPosition is null)
                {
                    KnobPosition = target;
                }
                else
                {
                    Animation.Start();
                }
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    private float? KnobPosition { get; set; }

    private UITimer Animation { get; }

    public PermissibilityControl()
    {
        Size = new Size(60, 20);
        Animation = new UITimer { Interval = 1.0 / 60.0 };
        Animation.Elapsed += OnAnimationTick;
    }

    private static float? Slot(Permissability permissability) => permissability switch
    {
        Permissability.Unset => null,
        Permissability.Deny => 0f,
        Permissability.Ask => 0.5f,
        Permissability.Always => 1f,
        
        _ => null
    };

    private static Color Colour(Permissability permissability) => permissability switch
    {
        Permissability.Unset => UnsetColour,
        Permissability.Deny => DenyColour,
        Permissability.Ask => AskColour,
        Permissability.Always => AlwaysColour,
        
        _ => Colors.Transparent
    };

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (Slot(State) is not float target || KnobPosition is not float current)
        {
            Animation.Stop();
            return;
        }

        float next = current + (target - current) * SlideStep;
        if (Math.Abs(target - next) < 0.01f)
        {
            next = target;
            Animation.Stop();
        }

        KnobPosition = next;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Buttons != MouseButtons.Primary) return;

        int segment = Math.Max(0, Math.Min(2, (int)(e.Location.X / (Width / 3f))));
        Permissability picked = segment switch
        {
            0 => Permissability.Deny,
            1 => Permissability.Ask,
            _ => Permissability.Always,
        };

        State = picked == State ? Permissability.Unset : picked;
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.AntiAlias = true;

        float height = Math.Min(Height - TrackInset * 2, 18f);
        float width = Width - TrackInset * 2;
        if (height <= 0 || width <= height) return;

        RectangleF track = new(TrackInset,(Height - height) / 2f, width, height);
        float radius = height / 2f;
        Color colour = Enabled ? Colour(State) : UnsetColour;

        using IGraphicsPath trackPath = GraphicsPath.GetRoundRect(track, radius);
        g.FillPath(new Color(colour, 0.25f), trackPath);
        g.DrawPath(new Pen(new Color(colour, 0.8f), 1f), trackPath);

        float travel = track.Width - height;
        float tickRadius = height * 0.12f;
        foreach (float slot in (float[])[0f, 0.5f, 1f])
        {
            PointF centre = new(track.X + radius + travel * slot, track.Y + radius);
            g.FillEllipse(new Color(colour, 0.6f), centre.X - tickRadius, centre.Y - tickRadius, tickRadius * 2, tickRadius * 2);
        }

        if (KnobPosition is not float position || State == Permissability.Unset) return;

        const float knobInset = 2f;
        float knobSize = height - knobInset * 2;
        RectangleF knob = new(track.X + travel * position + knobInset, track.Y + knobInset, knobSize, knobSize);
        g.FillEllipse(colour, knob);
        g.DrawEllipse(new Pen(Colors.White, 1.5f), knob);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Animation.Stop();
            Animation.Elapsed -= OnAnimationTick;
            Animation.Dispose();
        }
        base.Dispose(disposing);
    }

}

internal sealed class PermissionCell : CustomCell
{

    private IndirectBinding<Permissability> Bind { get; }

    protected override Control OnCreateCell(CellEventArgs args)
    {
        PermissibilityControl control = new ();
        control.BindDataContext(c => c.State, Bind);
        return control;
    }

    public PermissionCell(string argName)
    {
        Bind = Binding.Property<Permissability>(argName);
    }

    protected override void OnConfigureCell(CellEventArgs args, Control control)
    {
        control.DataContext = args.Item;
    }

}
