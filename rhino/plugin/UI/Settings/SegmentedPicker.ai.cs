#if R9
using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

internal sealed class SegmentedPicker<T> : Drawable where T : struct, Enum
{
    private const float TrackHeight = 24f;
    // The border stroke straddles the track's edge, so the track keeps clear of the control's bounds.
    private const float Margin = 1f;
    private const float Inset = 2f;
    private const float Radius = 6f;
    private const float IconSize = 11f;
    private const float IconGap = 5f;
    private const float SegmentPadding = 9f;
    private const float DimmedAlpha = 0.45f;

    private static Font LabelFont { get; } = SystemFonts.Default();
    private static Font GlyphFont { get; } = SystemFonts.Bold(IconSize - 3f);

    private T current;
    private T fallback;
    private bool isDimmed;
    private int? hovered;

    public SegmentedPicker(IReadOnlyList<Segment<T>> segments, T value)
    {
        Segments = segments;
        current = value;
        Size = new Size(PreferredWidth(segments), (int)(TrackHeight + 2f * Margin));
    }

    private IReadOnlyList<Segment<T>> Segments { get; }

    public event EventHandler<EventArgs>? ValueChanged;

    public T Value
    {
        get => current;
        set
        {
            if (EqualityComparer<T>.Default.Equals(current, value))
                return;

            current = value;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public T Fallback
    {
        get => fallback;
        set
        {
            fallback = value;
            Invalidate();
        }
    }

    public bool IsDimmed
    {
        get => isDimmed;
        set
        {
            isDimmed = value;
            Invalidate();
        }
    }

    public BindableBinding<SegmentedPicker<T>, T> ValueBinding
        => new(this, picker => picker.Value, (picker, value) => picker.Value = value, (picker, handler) => picker.ValueChanged += handler, (picker, handler) => picker.ValueChanged -= handler);

    public BindableBinding<SegmentedPicker<T>, T> FallbackBinding
        => new(this, picker => picker.Fallback, (picker, value) => picker.Fallback = value, null, null);

    public BindableBinding<SegmentedPicker<T>, bool> IsDimmedBinding
        => new(this, picker => picker.IsDimmed, (picker, value) => picker.IsDimmed = value, null, null);

    public static int PreferredWidth(IReadOnlyList<Segment<T>> segments)
        => (int)Math.Ceiling(TrackWidth(segments) + 2f * Margin);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Buttons != MouseButtons.Primary || SegmentAt(e.Location) is not int index)
            return;

        Value = Segments[index].Value;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Hover(SegmentAt(e.Location));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Hover(null);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.AntiAlias = true;

        float alpha = IsDimmed || !Enabled ? DimmedAlpha : 1f;
        Color text = SystemColors.ControlText;
        bool isDark = text.R * 0.299f + text.G * 0.587f + text.B * 0.114f > 0.5f;
        RectangleF track = Track();

        using (IGraphicsPath path = GraphicsPath.GetRoundRect(track, Radius))
        {
            graphics.FillPath(new Color(text, 0.06f * alpha), path);
            using Pen border = new(new Color(text, 0.18f * alpha), 1f);
            graphics.DrawPath(border, path);
        }

        int? chosen = IndexOf(Value);
        int? inherited = chosen is null ? IndexOf(Fallback) : null;
        for (int index = 0; index < Segments.Count; index++)
            DrawSegment(graphics, index, Chip(track, index), chosen == index, inherited == index, text, isDark, alpha);

        for (int index = 1; index < Segments.Count; index++)
        {
            if (index - 1 == chosen || index == chosen || index - 1 == inherited || index == inherited)
                continue;

            float x = track.X + Inset + index * SegmentWidthIn(track);
            graphics.DrawLine(new Color(text, 0.16f * alpha), x, track.Y + 6f, x, track.Bottom - 6f);
        }
    }

    private void DrawSegment(Graphics graphics, int index, RectangleF chip, bool isChosen, bool isInherited, Color text, bool isDark, float alpha)
    {
        Segment<T> segment = Segments[index];
        Color ink;
        using IGraphicsPath path = GraphicsPath.GetRoundRect(chip, Radius - Inset);

        if (isChosen)
        {
            graphics.FillPath(ChosenFill(segment.Style, text, alpha), path);
            ink = text;
        }
        else if (isInherited && segment.Style is SegmentStyle.Tinted tinted)
        {
            graphics.FillPath(new Color(tinted.Accent, 0.14f * alpha), path);
            using Pen outline = new(new Color(tinted.Accent, alpha), 1.25f);
            graphics.DrawPath(outline, path);
            ink = isDark ? tinted.Accent : tinted.OutlineInk;
        }
        else
        {
            if (hovered == index && Enabled)
                graphics.FillPath(new Color(text, 0.08f * alpha), path);
            ink = new Color(text, 0.75f);
        }

        ink = new Color(ink, ink.A * alpha);
        SizeF labelSize = LabelFont.MeasureString(segment.Label);
        float iconWidth = segment.Icon == SegmentIcon.None ? 0f : IconSize + IconGap;
        float left = chip.X + (chip.Width - iconWidth - labelSize.Width) / 2f;
        float middle = chip.Y + chip.Height / 2f;

        if (segment.Icon != SegmentIcon.None)
            DrawIcon(graphics, segment.Icon, new RectangleF(left, middle - IconSize / 2f, IconSize, IconSize), ink);

        using SolidBrush brush = new(ink);
        graphics.DrawText(LabelFont, brush, left + iconWidth, middle - LabelFont.LineHeight / 2f, segment.Label);
    }

    private static void DrawIcon(Graphics graphics, SegmentIcon icon, RectangleF box, Color ink)
    {
        using Pen pen = new(ink, 1.5f) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        RectangleF circle = box;
        circle.Inflate(-0.75f, -0.75f);

        switch (icon)
        {
            case SegmentIcon.Check:
                graphics.DrawLines(pen, new[] { At(box, 0.1f, 0.55f), At(box, 0.4f, 0.84f), At(box, 0.92f, 0.2f) });
                break;

            case SegmentIcon.Question:
                graphics.DrawEllipse(pen, circle);
                SizeF glyph = GlyphFont.MeasureString("?");
                graphics.DrawText(GlyphFont, ink, box.X + (box.Width - glyph.Width) / 2f, box.Y + (box.Height - GlyphFont.LineHeight) / 2f, "?");
                break;

            case SegmentIcon.Block:
                graphics.DrawEllipse(pen, circle);
                graphics.DrawLine(pen, At(box, 0.24f, 0.24f), At(box, 0.76f, 0.76f));
                break;

            case SegmentIcon.None:
            default:
                throw new ArgumentOutOfRangeException(nameof(icon), icon, null);
        }
    }

    private static PointF At(RectangleF box, float x, float y) => new(box.X + box.Width * x, box.Y + box.Height * y);

    private static Color ChosenFill(SegmentStyle style, Color text, float alpha) => style switch
    {
        SegmentStyle.Neutral => new Color(text, 0.22f * alpha),
        SegmentStyle.Tinted tinted => new Color(tinted.Accent, alpha),
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
    };

    private static float TrackWidth(IReadOnlyList<Segment<T>> segments) => segments.Count * NaturalSegmentWidth(segments) + 2f * Inset;

    private static float NaturalSegmentWidth(IReadOnlyList<Segment<T>> segments)
        => segments.Max(segment => LabelFont.MeasureString(segment.Label).Width + (segment.Icon == SegmentIcon.None ? 0f : IconSize + IconGap))
            + 2f * SegmentPadding;

    private int? IndexOf(T value)
    {
        for (int index = 0; index < Segments.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(Segments[index].Value, value))
                return index;
        }

        return null;
    }

    private void Hover(int? index)
    {
        if (hovered == index)
            return;

        hovered = index;
        Invalidate();
    }

    private RectangleF Track()
    {
        float width = Math.Min(Width - 2f * Margin, TrackWidth(Segments));
        float height = Math.Min(Height - 2f * Margin, TrackHeight);
        return new RectangleF((Width - width) / 2f, (Height - height) / 2f, width, height);
    }

    private float SegmentWidthIn(RectangleF track) => (track.Width - 2f * Inset) / Segments.Count;

    private RectangleF Chip(RectangleF track, int index)
    {
        float width = SegmentWidthIn(track);
        return new RectangleF(track.X + Inset + index * width, track.Y + Inset, width, track.Height - 2f * Inset);
    }

    private int? SegmentAt(PointF location)
    {
        RectangleF track = Track();
        if (!track.Contains(location))
            return null;

        int index = (int)((location.X - track.X - Inset) / SegmentWidthIn(track));
        return Math.Max(0, Math.Min(Segments.Count - 1, index));
    }
}
#endif
