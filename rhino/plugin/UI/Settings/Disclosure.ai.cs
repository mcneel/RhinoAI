#if R9
using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

internal sealed class Disclosure : Drawable
{
    private const int Extent = 18;
    private const float Arm = 4f;

    private bool isExpanded;

    public Disclosure()
    {
        Size = new Size(Extent, Extent);
    }

    public event EventHandler<EventArgs>? ExpandedChanged;

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
                return;

            isExpanded = value;
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public void Toggle() => IsExpanded = !IsExpanded;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Buttons != MouseButtons.Primary)
            return;

        Toggle();
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.AntiAlias = true;
        float x = Width / 2f;
        float y = Height / 2f;

        PointF[] chevron = IsExpanded
            ? [new(x - Arm, y - Arm / 2f), new(x, y + Arm / 2f), new(x + Arm, y - Arm / 2f)]
            : [new(x - Arm / 2f, y - Arm), new(x + Arm / 2f, y), new(x - Arm / 2f, y + Arm)];
        using Pen pen = new(Palette.Secondary, 1.6f) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        graphics.DrawLines(pen, chevron);
    }
}
#endif
