using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

internal sealed class DefaultStar : Drawable
{
    private const float Outer = 7f;
    private const float Inner = 3f;
    private static Color Gold { get; } = Color.FromArgb(0xF5, 0xB4, 0x2A);

    private bool isDefault;
    private bool isAvailable = true;
    private bool isHovered;

    public event EventHandler<EventArgs>? Pressed;

    public bool IsDefault
    {
        get => isDefault;
        set
        {
            isDefault = value;
            Invalidate();
        }
    }

    public bool IsAvailable
    {
        get => isAvailable;
        set
        {
            isAvailable = value;
            Invalidate();
        }
    }

    public BindableBinding<DefaultStar, bool> IsDefaultBinding
        => new(this, star => star.IsDefault, (star, value) => star.IsDefault = value, null, null);

    public BindableBinding<DefaultStar, bool> IsAvailableBinding
        => new(this, star => star.IsAvailable, (star, value) => star.IsAvailable = value, null, null);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Buttons != MouseButtons.Primary || !isAvailable)
            return;

        Pressed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        isHovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.AntiAlias = true;

        PointF centre = new(Width / 2f, Height / 2f + 0.5f);
        PointF[] points = new PointF[10];
        for (int index = 0; index < points.Length; index++)
        {
            double angle = -Math.PI / 2 + index * Math.PI / 5;
            float radius = index % 2 == 0 ? Outer : Inner;
            points[index] = new PointF(centre.X + radius * (float)Math.Cos(angle), centre.Y + radius * (float)Math.Sin(angle));
        }

        using GraphicsPath path = new();
        path.AddLines(points);
        path.CloseFigure();

        if (isDefault)
        {
            graphics.FillPath(isAvailable ? Gold : new Color(Gold, 0.35f), path);
            return;
        }

        if (!isAvailable)
            return;

        using Pen outline = new(new Color(SystemColors.ControlText, isHovered ? 0.7f : 0.28f), 1.1f) { LineJoin = PenLineJoin.Round };
        graphics.DrawPath(outline, path);
    }
}
