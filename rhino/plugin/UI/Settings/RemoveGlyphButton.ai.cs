#if R9
using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

internal sealed class RemoveGlyphButton : Drawable
{
    private const int Extent = 22;
    private const float Arm = 3.5f;

    private bool isHovered;

    public RemoveGlyphButton()
    {
        Size = new Size(Extent, Extent);
    }

    public event EventHandler<EventArgs>? Click;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Buttons != MouseButtons.Primary)
            return;

        Click?.Invoke(this, EventArgs.Empty);
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

        float x = Width / 2f;
        float y = Height / 2f;
        if (isHovered)
        {
            RectangleF box = new(x - Extent / 2f + 1f, y - Extent / 2f + 1f, Extent - 2f, Extent - 2f);
            using IGraphicsPath back = GraphicsPath.GetRoundRect(box, 5f);
            graphics.FillPath(new Color(Palette.Danger, 0.14f), back);
        }

        Color ink = isHovered ? Palette.Danger : Palette.Secondary;
        using Pen pen = new(ink, 1.5f) { LineCap = PenLineCap.Round };
        graphics.DrawLine(pen, x - Arm, y - Arm, x + Arm, y + Arm);
        graphics.DrawLine(pen, x - Arm, y + Arm, x + Arm, y - Arm);
    }
}
#endif
