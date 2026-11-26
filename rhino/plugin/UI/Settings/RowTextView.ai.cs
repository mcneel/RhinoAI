#if R9
using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

internal sealed class RowTextView : Drawable
{
    private const float IconSize = 24f;
    private const float IconGap = 8f;
    private const float LineGap = 1f;
    private const float BadgeGap = 6f;
    private const float BadgePaddingX = 5f;
    private const float BadgePaddingY = 1f;
    private const float EdgeInset = 2f;

    private static Font TitleFont { get; } = SystemFonts.Default();
    private static Font HeadingFont { get; } = SystemFonts.Bold();
    private static Font DetailFont { get; } = SystemFonts.Default(SystemFonts.Default().Size - 1);
    private static Font BadgeFont { get; } = SystemFonts.Default(SystemFonts.Default().Size - 2);

    private RowText? text;
    private Color textColor = SystemColors.ControlText;
    private bool isSelected;

    public RowText? Text
    {
        get => text;
        set
        {
            text = value;
            Invalidate();
        }
    }

    public Color TextColor
    {
        get => textColor;
        set
        {
            textColor = value;
            Invalidate();
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            isSelected = value;
            Invalidate();
        }
    }

    public BindableBinding<RowTextView, RowText?> TextBinding
        => new(this, view => view.Text, (view, value) => view.Text = value, null, null);

    public BindableBinding<RowTextView, Color> TextColorBinding
        => new(this, view => view.TextColor, (view, value) => view.TextColor = value, null, null);

    public BindableBinding<RowTextView, bool> IsSelectedBinding
        => new(this, view => view.IsSelected, (view, value) => view.IsSelected = value, null, null);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Text is not RowText row)
            return;

        Graphics graphics = e.Graphics;
        graphics.AntiAlias = true;
        float alpha = row.IsDimmed ? 0.5f : 1f;
        Color primary = new(TextColor, alpha);
        Color secondary = new(TextColor, 0.6f * alpha);

        float left = EdgeInset;
        if (row.Icon is RowIcon icon)
        {
            DrawIcon(graphics, icon, new RectangleF(left, (Height - IconSize) / 2f, IconSize, IconSize), primary);
            left += IconSize + IconGap;
        }

        Font titleFont = row.IsHeading ? HeadingFont : TitleFont;
        float titleHeight = titleFont.LineHeight;
        float detailHeight = DetailFont.LineHeight;
        float block = row.Detail.Length > 0 ? titleHeight + LineGap + detailHeight : titleHeight;
        float top = (Height - block) / 2f;
        float width = Math.Max(0f, Width - left - EdgeInset);

        DrawTitle(graphics, row, titleFont, primary, new RectangleF(left, top, width, titleHeight), alpha);
        DrawLine(graphics, DetailFont, secondary, new RectangleF(left, top + titleHeight + LineGap, width, detailHeight), row.Detail);
    }

    private void DrawTitle(Graphics graphics, RowText row, Font font, Color ink, RectangleF bounds, float alpha)
    {
        if (row.Badge is not RowBadge badge)
        {
            DrawLine(graphics, font, ink, bounds, row.Title);
            return;
        }

        SizeF size = BadgeSize(badge);
        float titleWidth = Math.Min(graphics.MeasureString(font, row.Title).Width, Math.Max(0f, bounds.Width - size.Width - BadgeGap));
        DrawLine(graphics, font, ink, new RectangleF(bounds.X, bounds.Y, titleWidth, bounds.Height), row.Title);
        DrawBadge(graphics, badge, new RectangleF(bounds.X + titleWidth + BadgeGap, bounds.Y + (bounds.Height - size.Height) / 2f, size.Width, size.Height), alpha);
    }

    private void DrawBadge(Graphics graphics, RowBadge badge, RectangleF bounds, float alpha)
    {
        Color ink = IsSelected ? TextColor : badge.Ink;
        using (IGraphicsPath path = GraphicsPath.GetRoundRect(bounds, bounds.Height / 2f))
            graphics.FillPath(new Color(ink, 0.14f * alpha), path);

        using SolidBrush brush = new(new Color(ink, alpha));
        graphics.DrawText(BadgeFont, brush, bounds.X + BadgePaddingX, bounds.Y + BadgePaddingY, badge.Text);
    }

    private static SizeF BadgeSize(RowBadge badge)
    {
        SizeF size = BadgeFont.MeasureString(badge.Text);
        return new SizeF(size.Width + 2f * BadgePaddingX, BadgeFont.LineHeight + 2f * BadgePaddingY);
    }

    private static void DrawLine(Graphics graphics, Font font, Color color, RectangleF bounds, string value)
    {
        if (bounds.Width <= 0f || value.Length == 0)
            return;

        using SolidBrush brush = new(color);
        graphics.DrawText(font, brush, bounds, value, FormattedTextWrapMode.None, FormattedTextAlignment.Left, FormattedTextTrimming.CharacterEllipsis);
    }

    private static void DrawIcon(Graphics graphics, RowIcon icon, RectangleF bounds, Color ink)
    {
        switch (icon)
        {
            case RowIcon.Picture picture:
            {
                graphics.DrawImage(picture.Image, bounds);
                break;
            }

            case RowIcon.Initial initial:
            {
                using (IGraphicsPath path = GraphicsPath.GetRoundRect(bounds, 6f))
                    graphics.FillPath(new Color(ink, 0.12f * ink.A), path);

                SizeF size = HeadingFont.MeasureString(initial.Letter);
                using SolidBrush brush = new(new Color(ink, 0.7f * ink.A));
                graphics.DrawText(HeadingFont, brush, bounds.X + (bounds.Width - size.Width) / 2f, bounds.Y + (bounds.Height - HeadingFont.LineHeight) / 2f, initial.Letter);
                break;
            }

            default:
                throw new InvalidOperationException("Unknown row icon.");
        }
    }
}
#endif
