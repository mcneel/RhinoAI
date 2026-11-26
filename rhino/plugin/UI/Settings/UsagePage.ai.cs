#if R9
using System.Linq.Expressions;

using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

internal sealed class UsagePage : Panel
{
    private const int TileGap = 8;
    private const float NumberGrowth = 7f;

    public UsagePage(UsagePageViewModel model)
    {
        DataContext = model;

        SegmentedPicker<UsageRange> range = UsageRanges.Picker();
        range.ValueBinding.BindDataContext((UsagePageViewModel m) => m.Range);

        UsageChart chart = new();
        chart.DataBinding.BindDataContext((UsagePageViewModel m) => m.Chart, DualBindingMode.OneWay);
        chart.EmptyTextBinding.BindDataContext((UsagePageViewModel m) => m.EmptyText, DualBindingMode.OneWay);

        StackLayout tiles = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = TileGap,
            Items =
            {
                Tile(m => m.TokensText, m => m.TokensCaption, m => m.TokensToolTip),
                Tile(m => m.CostText, m => m.CostCaption, m => m.CostToolTip),
            },
        };

        Content = new TableLayout
        {
            Spacing = new Size(5, 8),
            Rows =
            {
                new TableRow(new TableLayout(new TableRow(new TableCell(null, true), range))),
                new TableRow(tiles),
                new TableRow(new GroupBox { Padding = new Padding(10), Content = chart }) { ScaleHeight = true },
            },
        };
    }

    private static GroupBox Tile(
        Expression<Func<UsagePageViewModel, string>> value,
        Expression<Func<UsagePageViewModel, string>> caption,
        Expression<Func<UsagePageViewModel, string?>> toolTip)
    {
        Font numberFont = SystemFonts.Bold(SystemFonts.Default().Size + NumberGrowth);
        Font captionFont = SystemFonts.Default();

        Label number = new() { Font = numberFont };
        number.TextBinding.BindDataContext(value, DualBindingMode.OneWay);

        Label words = new() { Font = captionFont, TextColor = Palette.Secondary };
        words.TextBinding.BindDataContext(caption, DualBindingMode.OneWay);

        int baseline = (int)Math.Round(numberFont.Descent - captionFont.Descent);
        GroupBox tile = new()
        {
            Padding = new Padding(12, 6),
            Content = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                VerticalContentAlignment = VerticalAlignment.Bottom,
                Spacing = 6,
                Items = { number, new Panel { Padding = new Padding(0, 0, 0, baseline), Content = words } },
            },
        };
        tile.BindDataContext(control => control.ToolTip, toolTip, DualBindingMode.OneWay);
        return tile;
    }
}
#endif
