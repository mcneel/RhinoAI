#if R9
using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

internal sealed class StatusDot : Drawable
{
    private const int Diameter = 8;

    private bool isOn;

    public StatusDot()
    {
        Size = new Size(Diameter, Diameter);
    }

    public bool IsOn
    {
        get => isOn;
        set
        {
            isOn = value;
            Invalidate();
        }
    }

    public BindableBinding<StatusDot, bool> IsOnBinding
        => new(this, dot => dot.IsOn, (dot, value) => dot.IsOn = value, null, null);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.AntiAlias = true;
        RectangleF bounds = new((Width - Diameter) / 2f, (Height - Diameter) / 2f, Diameter, Diameter);
        e.Graphics.FillEllipse(IsOn ? Palette.Allow : Palette.SeriesOther, bounds);
    }
}
#endif
