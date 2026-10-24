using Eto.Forms;

using Rhino.AI.Statistics;

namespace Rhino.AI.UI;

// This page will show usage by agent and by plugin

internal sealed class AIUsagePage : AIPage
{

    // Total token usage (nice chunky label)

    // Over time is important, so today, week, current month, current year / all time

    // A bar chart where each bar is a plugin and each bar is broken up into model usage

    // plugins with < a certian % of tokens, maybe < 2% of tokens
    // shouldn't be listed (with a note explaining this)

    private AIUsageViewModel Model => (DataContext as AIUsageViewModel)!;

    public AIUsagePage()
    {
        DataContext = new AIUsageViewModel();
        InitLayout();
    }

    private void InitLayout()
    {
        DynamicLayout layout = new();
        layout.BeginVertical();

        // layout.Add

        // TODO : I would like things to NOT be selectable in this grid view
        GridView view = new()
        {
            SelectedRow = -1, // Default to nothing selected

            Enabled = false,
            AllowDrop = false,
            ShowHeader = false,
            Border = BorderType.Line,
            GridLines = GridLines.None,
            AllowEmptySelection = true,
            AllowColumnReordering = false,
            AllowMultipleSelection = false,
            CanDeleteItem = (_) => false,
            Columns =
            {
                new GridColumn()
                {
                    DataCell = new StatisticCell()
                }
            }
        };

        layout.Add(view, true, true);

        layout.EndVertical();

        Content = layout;
    }

    public override bool Validate() => true;
    public override bool TryApplyChanges() => true;

}

internal sealed class StatisticCell : CustomCell
{
    protected override Control OnCreateCell(CellEventArgs args)
    {
        ImageView icon = new();
        // icon.BindDataContext(i => i.Image, Binding.Property((Statistics s) => s.Image));

        Label tokenCount = new();
        // tokenCount.BindDataContext(tc => tc.Text, Binding.Property((Statistics s) => s.Name));

        ModelBar modelBreakdown = new();
        modelBreakdown.BindDataContext(tc => tc.ModelList, Binding.Property((PlugInStatistics s) => s.Statistics).Cast<List<PlugInModelStatistic>?>());

        DynamicLayout data = new();
        data.BeginVertical();
        data.Add(tokenCount);
        data.Add(modelBreakdown);
        data.EndVertical();

        DynamicLayout overview = new();
        overview.BeginHorizontal();
        overview.Add(icon, false, false);
        overview.Add(data);
        overview.EndHorizontal();

        // TODO : Give the cell a width based on its % of token usage, logarithmically I think. The % or w/e should have a min / max width

        return overview;
    }

    protected override void OnConfigureCell(CellEventArgs args, Control control)
    {
        control.DataContext = args.Item;
    }

}

internal sealed class ModelBar : Drawable
{

    public List<PlugInModelStatistic> ModelList { get; } = [];

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // TODO : Claude - Draw a bar that is composed of all the models in the list
        //                 Use vendor colours for each model, with each model having a slightly different shade
        //                 Maybe alphabetical and just 0.1% less opacity or w/e idk
        //                 Each bar item in the bar should have the display name of the model
    }

}

internal sealed class AIUsageViewModel : SimpleViewModel
{

    public int OverallTotal { get; } = 0;

    public List<PlugInStatistics> Stats { get; } = [];

    public AIUsageViewModel()
    {
        IEnumerable<PlugIns.PlugInToken> tokens = PlugIns.PlugInRegistry.PlugIns.Keys;
        foreach (PlugIns.PlugInToken token in tokens)
        {
            PlugInStatistics? stats = PlugInStatistics.Usage(token);
            if (stats is null) continue;

            OverallTotal += stats.TotalTokenUsage;
            Stats.Add(stats);
        }
    }

}
