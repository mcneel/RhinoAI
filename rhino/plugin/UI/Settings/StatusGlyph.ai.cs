using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

internal sealed class StatusGlyph : Drawable
{
    private const int Extent = 18;

    private StatusTone tone = StatusTone.Neutral;

    public StatusGlyph()
    {
        Size = new Size(Extent, Extent);
    }

    public StatusTone Tone
    {
        get => tone;
        set
        {
            tone = value;
            Invalidate();
        }
    }

    public BindableBinding<StatusGlyph, StatusTone> ToneBinding
        => new(this, glyph => glyph.Tone, (glyph, value) => glyph.Tone = value, null, null);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.AntiAlias = true;

        Color ink = AgentStatuses.Colour(tone);
        float x = Width / 2f;
        float y = Height / 2f;
        RectangleF circle = new(x - 7.5f, y - 7.5f, 15f, 15f);
        graphics.FillEllipse(new Color(ink, 0.16f), circle);

        using Pen pen = new(ink, 1.6f) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        graphics.DrawEllipse(pen, circle);
        switch (tone)
        {
            case StatusTone.Ok:
                graphics.DrawLines(pen, new[] { new PointF(x - 3.5f, y + 0.2f), new PointF(x - 1f, y + 2.7f), new PointF(x + 3.6f, y - 2.6f) });
                break;

            case StatusTone.Warn:
                graphics.DrawLine(pen, x, y - 3.6f, x, y + 0.8f);
                graphics.FillEllipse(ink, new RectangleF(x - 1f, y + 2.4f, 2f, 2f));
                break;

            case StatusTone.Neutral:
                graphics.DrawLine(pen, x - 3f, y, x + 3f, y);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(tone), tone, null);
        }
    }
}
