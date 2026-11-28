using System.Globalization;

using Eto.Drawing;
using Eto.Forms;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal sealed class UsageChart : Drawable
{
    private const int TickCount = 4;
    private const float LegendDot = 8f;
    private const float LegendInnerGap = 6f;
    private const float LegendItemGap = 18f;
    private const float LegendRowGap = 4f;
    private const float PlotTopGap = 14f;
    private const float AxisGap = 6f;
    private const float AxisLabelGap = 10f;
    private const float BarMaxWidth = 28f;
    private const float BarFill = 0.62f;
    private const float BarRadius = 3f;
    private const float CalloutPadding = 8f;
    private const float CalloutGap = 6f;
    private const float CalloutRowGap = 3f;
    private const int HourLabelEvery = 6;

    private static Font LegendFont { get; } = SystemFonts.Default();
    private static Font AxisFont { get; } = SystemFonts.Default(SystemFonts.Default().Size - 2f);
    private static Font CalloutTitleFont { get; } = SystemFonts.Bold(SystemFonts.Default().Size - 1f);
    private static Font CalloutFont { get; } = SystemFonts.Default(SystemFonts.Default().Size - 1f);

    private UsageChartData? data;
    private string emptyText = string.Empty;
    private int? hovered;
    private RectangleF plot;
    private RectangleF totalItem;

    public UsageChartData? Data
    {
        get => data;
        set
        {
            data = value;
            hovered = null;
            Invalidate();
        }
    }

    public string EmptyText
    {
        get => emptyText;
        set
        {
            emptyText = value;
            Invalidate();
        }
    }

    public string? TotalToolTip { get; set; }

    public BindableBinding<UsageChart, UsageChartData?> DataBinding
        => new(this, chart => chart.Data, (chart, value) => chart.Data = value, null, null);

    public BindableBinding<UsageChart, string> EmptyTextBinding
        => new(this, chart => chart.EmptyText, (chart, value) => chart.EmptyText = value, null, null);

    public BindableBinding<UsageChart, string?> TotalToolTipBinding
        => new(this, chart => chart.TotalToolTip, (chart, value) => chart.TotalToolTip = value, null, null);

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Hover(BucketAt(e.Location));
        ShowToolTip(totalItem.Contains(e.Location) ? TotalToolTip : null);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Hover(null);
        ShowToolTip(null);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.AntiAlias = true;
        plot = RectangleF.Empty;
        if (Data is not UsageChartData chart)
            return;

        Color text = SystemColors.ControlText;
        Color secondary = new(text, 0.6f);
        float top = DrawLegend(graphics, chart, text, secondary);

        if (chart.Peak == 0 || chart.Buckets.Count == 0)
        {
            DrawCentred(graphics, EmptyText, secondary, new RectangleF(0f, top, Width, Height - top));
            return;
        }

        (double max, double step) = UsageFormat.NiceScale(chart.Peak, TickCount);
        int ticks = (int)Math.Round(max / step);
        string[] tickLabels = Enumerable.Range(0, ticks + 1).Select(tick => UsageFormat.Compact((long)(tick * step), Culture)).ToArray();
        float labelWidth = tickLabels.Max(label => AxisFont.MeasureString(label).Width);
        plot = new RectangleF(
            labelWidth + AxisGap,
            top + PlotTopGap,
            Math.Max(0f, Width - labelWidth - AxisGap - 1f),
            Math.Max(0f, Height - top - PlotTopGap - AxisFont.LineHeight - AxisGap));
        float slot = plot.Width / chart.Buckets.Count;

        for (int tick = 0; tick <= ticks; tick++)
        {
            float y = plot.Bottom - (float)(tick * step / max) * plot.Height;
            graphics.DrawLine(new Color(text, tick == 0 ? 0.25f : 0.08f), plot.X, y, plot.Right, y);
            SizeF size = AxisFont.MeasureString(tickLabels[tick]);
            graphics.DrawText(AxisFont, secondary, plot.X - AxisGap - size.Width, y - AxisFont.LineHeight / 2f, tickLabels[tick]);
        }

        if (hovered is int column)
            graphics.FillRectangle(new Color(text, 0.06f), plot.X + slot * column, plot.Y, slot, plot.Height);

        for (int index = 0; index < chart.Buckets.Count; index++)
            DrawBar(graphics, chart, index, slot, max);

        DrawAxisLabels(graphics, chart, slot, secondary);

        if (hovered is int bucket)
            DrawCallout(graphics, chart, bucket, slot, max, text, secondary);
    }

    private float DrawLegend(Graphics graphics, UsageChartData chart, Color text, Color secondary)
    {
        totalItem = RectangleF.Empty;
        if (chart.Series.Count == 0)
            return 0f;

        float x = 0f;
        float y = 0f;
        float line = LegendFont.LineHeight;

        RectangleF Item(Color? dot, string name, long value)
        {
            string total = UsageFormat.Compact(value, Culture);
            float marker = dot is null ? 0f : LegendDot + LegendInnerGap;
            float nameWidth = LegendFont.MeasureString(name).Width;
            float itemWidth = marker + nameWidth + LegendInnerGap + LegendFont.MeasureString(total).Width;
            if (x > 0f && x + itemWidth > Width)
            {
                x = 0f;
                y += line + LegendRowGap;
            }

            if (dot is Color colour)
                graphics.FillEllipse(colour, x, y + (line - LegendDot) / 2f, LegendDot, LegendDot);
            graphics.DrawText(LegendFont, text, x + marker, y, name);
            graphics.DrawText(LegendFont, secondary, x + marker + nameWidth + LegendInnerGap, y, total);
            RectangleF bounds = new(x, y, itemWidth, line);
            x += itemWidth + LegendItemGap;
            return bounds;
        }

        totalItem = Item(null, LOC.STR("Total"), chart.Total);
        foreach (UsageSeries series in chart.Series)
            Item(Colour(series), Name(chart.Split, series), series.Total);

        return y + line;
    }

    private void DrawBar(Graphics graphics, UsageChartData chart, int index, float slot, double max)
    {
        UsageBucket bucket = chart.Buckets[index];
        if (bucket.Total == 0)
            return;

        float width = Math.Max(2f, Math.Min(BarMaxWidth, slot * BarFill));
        float x = plot.X + slot * index + (slot - width) / 2f;
        int topSeries = Enumerable.Range(0, bucket.Values.Count).Last(series => bucket.Values[series] > 0);
        float bottom = plot.Bottom;

        for (int series = 0; series < bucket.Values.Count; series++)
        {
            long value = bucket.Values[series];
            if (value == 0)
                continue;

            float height = (float)(value / max) * plot.Height;
            Color colour = Colour(chart.Series[series]);
            if (series == topSeries && width >= 2f * BarRadius && height >= BarRadius)
            {
                using IGraphicsPath path = GraphicsPath.GetRoundRect(new RectangleF(x, bottom - height, width, height), BarRadius, BarRadius, 0f, 0f);
                graphics.FillPath(colour, path);
            }
            else
            {
                float gap = series == topSeries || height <= 2f ? 0f : 1f;
                graphics.FillRectangle(colour, x, bottom - height + gap, width, height - gap);
            }

            bottom -= height;
        }
    }

    private void DrawAxisLabels(Graphics graphics, UsageChartData chart, float slot, Color secondary)
    {
        int count = chart.Buckets.Count;
        string[] labels = Enumerable.Range(0, count).Select(index => AxisLabel(chart, index)).ToArray();
        float widest = labels.Max(label => AxisFont.MeasureString(label).Width);
        int every = Math.Max(1, (int)Math.Ceiling((widest + AxisLabelGap) / slot));
        if (chart.Grain == UsageGrain.Hour)
            every = Math.Max(HourLabelEvery, every);

        for (int index = 0; index < count; index++)
        {
            bool show = chart.Grain == UsageGrain.Hour ? index % every == 0 : (count - 1 - index) % every == 0;
            if (!show)
                continue;

            float labelWidth = AxisFont.MeasureString(labels[index]).Width;
            float left = plot.X + slot * (index + 0.5f) - labelWidth / 2f;
            left = Math.Max(0f, Math.Min(Width - labelWidth, left));
            graphics.DrawText(AxisFont, secondary, left, plot.Bottom + AxisGap, labels[index]);
        }
    }

    private void DrawCallout(Graphics graphics, UsageChartData chart, int index, float slot, double max, Color text, Color secondary)
    {
        UsageBucket bucket = chart.Buckets[index];
        List<(Color? Dot, string Name, string Value)> rows = [];
        for (int series = 0; series < bucket.Values.Count; series++)
        {
            if (bucket.Values[series] > 0)
                rows.Add((Colour(chart.Series[series]), Name(chart.Split, chart.Series[series]), UsageFormat.Compact(bucket.Values[series], Culture)));
        }
        if (rows.Count > 1)
            rows.Add((null, LOC.STR("Total"), UsageFormat.Compact(bucket.Total, Culture)));

        string title = CalloutTitle(chart.Grain, bucket.Start);
        float nameColumn = rows.Max(row => CalloutFont.MeasureString(row.Name).Width) + LegendDot + LegendInnerGap;
        float valueColumn = rows.Max(row => CalloutFont.MeasureString(row.Value).Width);
        float width = Math.Max(CalloutTitleFont.MeasureString(title).Width, nameColumn + LegendItemGap + valueColumn) + 2f * CalloutPadding;
        float height = CalloutTitleFont.LineHeight + CalloutRowGap + rows.Count * (CalloutFont.LineHeight + CalloutRowGap) + 2f * CalloutPadding;

        float barTop = plot.Bottom - (float)(bucket.Total / max) * plot.Height;
        float x = plot.X + slot * (index + 1) + CalloutGap;
        if (x + width > Width)
            x = plot.X + slot * index - CalloutGap - width;
        x = Math.Max(0f, x);
        float y = Math.Max(0f, Math.Min(plot.Bottom - height, barTop - height / 2f));

        RectangleF box = new(x, y, width, height);
        using (IGraphicsPath path = GraphicsPath.GetRoundRect(box, 6f))
        {
            graphics.FillPath(SystemColors.ControlBackground, path);
            using Pen border = new(new Color(text, 0.18f), 1f);
            graphics.DrawPath(border, path);
        }

        float rowY = y + CalloutPadding;
        graphics.DrawText(CalloutTitleFont, text, x + CalloutPadding, rowY, title);
        rowY += CalloutTitleFont.LineHeight + CalloutRowGap;
        foreach ((Color? dot, string name, string value) in rows)
        {
            if (dot is Color colour)
                graphics.FillEllipse(colour, x + CalloutPadding, rowY + (CalloutFont.LineHeight - LegendDot) / 2f, LegendDot, LegendDot);
            graphics.DrawText(CalloutFont, dot is null ? text : secondary, x + CalloutPadding + LegendDot + LegendInnerGap, rowY, name);
            float valueWidth = CalloutFont.MeasureString(value).Width;
            graphics.DrawText(CalloutFont, text, x + width - CalloutPadding - valueWidth, rowY, value);
            rowY += CalloutFont.LineHeight + CalloutRowGap;
        }
    }

    private static void DrawCentred(Graphics graphics, string message, Color colour, RectangleF bounds)
    {
        if (message.Length == 0)
            return;

        float middle = bounds.Y + bounds.Height / 2f;
        SizeF size = LegendFont.MeasureString(message);
        if (size.Width <= bounds.Width)
        {
            graphics.DrawText(LegendFont, colour, bounds.X + (bounds.Width - size.Width) / 2f, middle - LegendFont.LineHeight / 2f, message);
            return;
        }

        using SolidBrush brush = new(colour);
        RectangleF block = new(bounds.X, middle - LegendFont.LineHeight, bounds.Width, bounds.Bottom - middle + LegendFont.LineHeight);
        graphics.DrawText(LegendFont, brush, block, message, FormattedTextWrapMode.Word, FormattedTextAlignment.Center, FormattedTextTrimming.None);
    }

    private static string AxisLabel(UsageChartData chart, int index)
    {
        DateTime start = chart.Buckets[index].Start;
        bool isLast = index == chart.Buckets.Count - 1;
        return chart.Grain switch
        {
            UsageGrain.Hour => start.ToString(Is24Hour ? "HH:mm" : "h tt", Culture),
            UsageGrain.Day when isLast => LOC.STR("Today"),
            UsageGrain.Day when chart.Buckets.Count <= 7 => start.ToString("ddd", Culture),
            UsageGrain.Day => start.ToString("MMM d", Culture),
            UsageGrain.Week => start.ToString("MMM d", Culture),
            UsageGrain.Month when index == 0 || start.Month == 1 => start.ToString("MMM yyyy", Culture),
            UsageGrain.Month => start.ToString("MMM", Culture),
            _ => throw new ArgumentOutOfRangeException(nameof(chart), chart.Grain, null),
        };
    }

    private static string CalloutTitle(UsageGrain grain, DateTime start) => grain switch
    {
        UsageGrain.Hour => string.Format(LOC.STR("{0} to {1}"), start.ToString("t", Culture), start.AddHours(1).ToString("t", Culture)),
        UsageGrain.Day => start.ToString("ddd, MMM d", Culture),
        UsageGrain.Week => string.Format(LOC.STR("Week of {0}"), start.ToString("MMM d", Culture)),
        UsageGrain.Month => start.ToString("MMMM yyyy", Culture),
        _ => throw new ArgumentOutOfRangeException(nameof(grain), grain, null),
    };

    private static string Name(UsageSplit split, UsageSeries series) => series switch
    {
        UsageSeries.Named named => named.Name,
        UsageSeries.Others others => split switch
        {
            UsageSplit.Agent => Phrase.Count(others.Count, LOC.STR("1 other agent"), LOC.STR("{0} other agents")),
            UsageSplit.Model => Phrase.Count(others.Count, LOC.STR("1 other model"), LOC.STR("{0} other models")),
            _ => throw new ArgumentOutOfRangeException(nameof(split), split, null),
        },
        _ => throw new InvalidOperationException("Unknown usage series."),
    };

    private static Color Colour(UsageSeries series) => series switch
    {
        UsageSeries.Named named => Palette.Series(named.Colour),
        UsageSeries.Others => Palette.SeriesOther,
        _ => throw new InvalidOperationException("Unknown usage series."),
    };

    private static bool Is24Hour => Culture.DateTimeFormat.ShortTimePattern.Contains("H");

    private void Hover(int? index)
    {
        if (hovered == index)
            return;

        hovered = index;
        Invalidate();
    }

    private void ShowToolTip(string? text)
    {
        if (ToolTip != text)
            ToolTip = text;
    }

    private int? BucketAt(PointF location)
    {
        if (Data is not UsageChartData chart || chart.Buckets.Count == 0 || plot.Width <= 0f)
            return null;
        if (location.X < plot.X || location.X >= plot.Right || location.Y < plot.Y - PlotTopGap || location.Y > plot.Bottom)
            return null;

        int index = (int)((location.X - plot.X) / (plot.Width / chart.Buckets.Count));
        return index >= 0 && index < chart.Buckets.Count && chart.Buckets[index].Total > 0 ? index : null;
    }
}
