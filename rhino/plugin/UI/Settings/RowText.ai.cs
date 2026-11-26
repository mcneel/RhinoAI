#if R9
using Eto.Drawing;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal interface IRowItem
{
    public RowText Text { get; }

    public string? ToolTip { get; }
}

internal sealed record RowText(string Title, string Detail, bool IsHeading, RowBadge? Badge, RowIcon? Icon, bool IsDimmed);

internal sealed record RowBadge(string Text, Color Ink)
{
    public static RowBadge Destructive { get; } = new(LOC.STR("Destructive"), Palette.Danger);

    public static RowBadge ReadOnly { get; } = new(LOC.STR("Read only"), Palette.Allow);
}

internal abstract record RowIcon
{
    private RowIcon() { }

    public sealed record Picture(Image Image) : RowIcon;
    public sealed record Initial(string Letter) : RowIcon;
}
#endif
