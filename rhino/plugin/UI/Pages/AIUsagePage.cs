using System.Collections.ObjectModel;

using Eto.Forms;
using Eto.Drawing;

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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Model.Update();
    }

    private void InitLayout()
    {
        DynamicLayout layout = new();
        layout.BeginVertical();

        Label total = new() { Font = Fonts.Sans(16, FontStyle.Bold) };
        total.TextBinding.BindDataContext(Binding.Property((AIUsageViewModel vm) => vm.OverallTotal).Convert(t => string.Format("{0:N0} tokens", t)));
        layout.Add(total);

        // TODO : I would like things to NOT be selectable in this grid view
        GridView view = new()
        {
            SelectedRow = -1, // Default to nothing selected
            DataStore = Model.Stats,
            RowHeight = 44,

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
                    DataCell = new StatisticCell(),
                    Expand = true,
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
        tokenCount.BindDataContext(tc => tc.Text, Binding.Delegate((PlugInUsage u) => string.Format("{0} : {1:N0} tokens", u.Name, u.TokenCount)));

        ModelBar modelBreakdown = new();
        modelBreakdown.BindDataContext(tc => tc.ModelList, Binding.Property((PlugInUsage u) => u.Models).Cast<List<PlugInModelStatistic>?>());

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

    private const float ShadeStep = 0.15f;
    private const float MinShade = 0.4f;
    private const float TextPadding = 4f;

    private static Font LabelFont { get; } = Fonts.Sans(9);

    private List<PlugInModelStatistic>? _modelList;
    public List<PlugInModelStatistic>? ModelList
    {
        get => _modelList;
        set
        {
            _modelList = value;
            Invalidate();
        }
    }

    public ModelBar()
    {
        MinimumSize = new Size(-1, 20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (ModelList is null) return;

        float total = ModelList.Sum(m => m.TokenCount);
        if (total <= 0) return;

        RectangleF bounds = new(Size);
        float x = bounds.X;

        IEnumerable<IGrouping<string, PlugInModelStatistic>> byVendor = ModelList
            .Where(m => m.TokenCount > 0)
            .OrderBy(m => m.Vendor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.ModelName, StringComparer.OrdinalIgnoreCase)
            .GroupBy(m => m.Vendor, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, PlugInModelStatistic> vendor in byVendor)
        {
            Color baseColour = VendorColour(vendor.Key);
            int index = 0;
            foreach (PlugInModelStatistic model in vendor)
            {
                float width = bounds.Width * (model.TokenCount / total);
                RectangleF segment = new(x, bounds.Y, width, bounds.Height);

                float shade = Math.Max(MinShade, 1f - (ShadeStep * index));
                e.Graphics.FillRectangle(new Color(baseColour, shade), segment);
                DrawLabel(e.Graphics, segment, model.ModelName);

                x += width;
                index++;
            }
        }
    }

    private static void DrawLabel(Graphics graphics, RectangleF segment, string text)
    {
        SizeF textSize = graphics.MeasureString(LabelFont, text);
        if (textSize.Width + (TextPadding * 2) > segment.Width) return;

        PointF location = new(
            segment.X + ((segment.Width - textSize.Width) / 2f),
            segment.Y + ((segment.Height - textSize.Height) / 2f));
        graphics.DrawText(LabelFont, Colors.White, location, text);
    }

    private static Color VendorColour(string vendor) => vendor.ToLowerInvariant() switch
    {
        "anthropic" => Color.FromArgb(0xD9, 0x77, 0x57),
        "openai" => Color.FromArgb(0x10, 0xA3, 0x7F),
        "google" => Color.FromArgb(0x42, 0x85, 0xF4),
        "deepseek" => Color.FromArgb(0x4D, 0x6B, 0xFE),
        "lm studio" => Color.FromArgb(0x7C, 0x4D, 0xFF),
        "apple" => Color.FromArgb(0x55, 0x55, 0x55),
        
        _ => Colors.SlateGray,
    };

}

internal sealed class AIUsageViewModel : SimpleViewModel
{

    private int overallTotal;
    public int OverallTotal { get => overallTotal; private set => SetProperty(ref overallTotal, value); }

    public ObservableCollection<PlugInUsage> Stats { get; } = [];

    public AIUsageViewModel()
    {
        Update();
    }

    public void Update()
    {
        Stats.Clear();
        int total = 0;
        IEnumerable<PlugIns.PlugInToken> tokens = PlugIns.PlugInRegistry.PlugIns.Keys;
        foreach (PlugIns.PlugInToken token in tokens)
        {
            PlugInStatistics? stats = PlugInStatistics.Usage(token);
            if (stats is null) continue;

            total += stats.TotalTokenUsage;
            Stats.Add(new PlugInUsage(token.Name, stats.TotalTokenUsage, stats.Statistics));
        }

        OverallTotal = total;
    }

}

internal sealed record PlugInUsage(string Name, int TokenCount, List<PlugInModelStatistic> Models);
