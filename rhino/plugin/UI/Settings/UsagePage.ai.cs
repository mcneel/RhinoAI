using Eto.Drawing;
using Eto.Forms;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal sealed class UsagePage : Panel
{
    public UsagePage(UsagePageViewModel model)
    {
        DataContext = model;

        SegmentedPicker<UsageRange> range = UsageRanges.Picker();
        range.ValueBinding.BindDataContext((UsagePageViewModel m) => m.Range);

        UsageChart chart = new();
        chart.DataBinding.BindDataContext((UsagePageViewModel m) => m.Chart, DualBindingMode.OneWay);
        chart.EmptyTextBinding.BindDataContext((UsagePageViewModel m) => m.EmptyText, DualBindingMode.OneWay);
        chart.TotalToolTipBinding.BindDataContext((UsagePageViewModel m) => m.TotalToolTip, DualBindingMode.OneWay);

        UsageBadges plugIns = new(model.Toggle, model.IconOf) { ToolTip = LOC.STR("Select plug-ins to chart only their usage.") };
        plugIns.BadgesBinding.BindDataContext((UsagePageViewModel m) => m.Badges, DualBindingMode.OneWay);
        plugIns.SelectedBinding.BindDataContext((UsagePageViewModel m) => m.SelectedPlugIns, DualBindingMode.OneWay);

        Content = new TableLayout
        {
            Spacing = new Size(5, 8),
            Rows =
            {
                new TableRow(new TableLayout(new TableRow(new TableCell(null, true), range))),
                new TableRow(new GroupBox { Padding = new Padding(10), Content = chart }) { ScaleHeight = true },
                new TableRow(plugIns),
            },
        };
    }
}
