using System.Globalization;

using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

internal sealed class UsageBadges : Drawable
{
    // The outline stroke straddles a badge's edge, so badges keep clear of the control's bounds.
    private const float Margin = 1f;
    private const float BadgeHeight = 24f;
    private const float Radius = 6f;
    private const float BadgePadding = 8f;
    private const float BadgeGap = 6f;
    private const float IconSize = 16f;
    private const float InnerGap = 6f;

    private static Font LabelFont { get; } = SystemFonts.Default();
    private static Font InitialFont { get; } = SystemFonts.Bold(SystemFonts.Default().Size - 3f);

    private IReadOnlyList<PlugInBadge> badges = [];
    private IReadOnlyCollection<string> selected = [];
    private int? hovered;

    public UsageBadges(Action<string> toggle, Func<string, Image?> iconOf)
    {
        Toggle = toggle;
        IconOf = iconOf;
        Visible = false;
    }

    private Action<string> Toggle { get; }

    private Func<string, Image?> IconOf { get; }

    public IReadOnlyList<PlugInBadge> Badges
    {
        get => badges;
        set
        {
            badges = value;
            hovered = null;
            Visible = badges.Count > 0;
            Fit();
            Invalidate();
        }
    }

    public IReadOnlyCollection<string> Selected
    {
        get => selected;
        set
        {
            selected = value;
            Invalidate();
        }
    }

    // Eto pushes null before the control inherits its DataContext.
    public BindableBinding<UsageBadges, IReadOnlyList<PlugInBadge>?> BadgesBinding
        => new(this, view => view.Badges, (view, value) => view.Badges = value ?? [], null, null);

    public BindableBinding<UsageBadges, IReadOnlyCollection<string>?> SelectedBinding
        => new(this, view => view.Selected, (view, value) => view.Selected = value ?? [], null, null);

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Fit();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Buttons != MouseButtons.Primary || BadgeAt(e.Location) is not int index)
            return;

        Toggle(Badges[index].Id);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Hover(BadgeAt(e.Location));
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

        IReadOnlyList<RectangleF> boxes = Layout();
        for (int index = 0; index < Badges.Count; index++)
            DrawBadge(graphics, Badges[index], boxes[index], Selected.Contains(Badges[index].Id), hovered == index);
    }

    private void DrawBadge(Graphics graphics, PlugInBadge badge, RectangleF box, bool isSelected, bool isHovered)
    {
        Color text = SystemColors.ControlText;
        using (IGraphicsPath path = GraphicsPath.GetRoundRect(box, Radius))
        {
            graphics.FillPath(new Color(text, isSelected ? 0.2f : isHovered ? 0.1f : 0.05f), path);
            using Pen outline = new(new Color(text, isSelected ? 0.45f : 0.18f), 1f);
            graphics.DrawPath(outline, path);
        }

        float middle = box.Y + box.Height / 2f;
        float x = box.X + BadgePadding;
        DrawIcon(graphics, badge, new RectangleF(x, middle - IconSize / 2f, IconSize, IconSize), text);
        x += IconSize + InnerGap;
        graphics.DrawText(LabelFont, text, x, middle - LabelFont.LineHeight / 2f, badge.Name);
        x += LabelFont.MeasureString(badge.Name).Width + InnerGap;
        graphics.DrawText(LabelFont, new Color(text, 0.6f), x, middle - LabelFont.LineHeight / 2f, Total(badge));
    }

    private void DrawIcon(Graphics graphics, PlugInBadge badge, RectangleF bounds, Color text)
    {
        if (IconOf(badge.Id) is Image icon)
        {
            graphics.DrawImage(icon, bounds);
            return;
        }

        using (IGraphicsPath path = GraphicsPath.GetRoundRect(bounds, 4f))
            graphics.FillPath(new Color(text, 0.12f), path);

        string initial = badge.Name.Where(char.IsLetterOrDigit).Select(letter => char.ToUpper(letter, Culture).ToString()).FirstOrDefault() ?? "?";
        SizeF size = InitialFont.MeasureString(initial);
        graphics.DrawText(InitialFont, new Color(text, 0.7f), bounds.X + (bounds.Width - size.Width) / 2f, bounds.Y + (bounds.Height - InitialFont.LineHeight) / 2f, initial);
    }

    private static string Total(PlugInBadge badge) => UsageFormat.Compact(badge.Total, Culture);

    private static float WidthOf(PlugInBadge badge)
        => BadgePadding + IconSize + InnerGap + LabelFont.MeasureString(badge.Name).Width + InnerGap + LabelFont.MeasureString(Total(badge)).Width + BadgePadding;

    private IReadOnlyList<RectangleF> Layout()
    {
        float right = Width > 0 ? Width - Margin : float.MaxValue;
        List<RectangleF> boxes = new(Badges.Count);
        float x = Margin;
        float y = Margin;
        foreach (PlugInBadge badge in Badges)
        {
            float width = WidthOf(badge);
            if (x > Margin && x + width > right)
            {
                x = Margin;
                y += BadgeHeight + BadgeGap;
            }

            boxes.Add(new RectangleF(x, y, width, BadgeHeight));
            x += width + BadgeGap;
        }

        return boxes;
    }

    private void Fit()
    {
        IReadOnlyList<RectangleF> boxes = Layout();
        int height = boxes.Count == 0 ? 0 : (int)Math.Ceiling(boxes.Max(box => box.Bottom) + Margin);
        if (Height != height)
            Height = height;
    }

    private void Hover(int? index)
    {
        if (hovered == index)
            return;

        hovered = index;
        Invalidate();
    }

    private int? BadgeAt(PointF location)
    {
        IReadOnlyList<RectangleF> boxes = Layout();
        for (int index = 0; index < boxes.Count; index++)
        {
            if (boxes[index].Contains(location))
                return index;
        }

        return null;
    }
}
