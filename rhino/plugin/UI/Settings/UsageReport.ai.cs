namespace Rhino.AI.UI.Settings;

internal enum UsageRange { Today, Week, Month, AllTime }

internal enum UsageGrain { Hour, Day, Week, Month }

internal enum UsageSplit { Agent, Model }

internal sealed record UsageEntry(DateTimeOffset At, string AgentId, string AgentName, string? Vendor, int InputTokens, int OutputTokens)
{
    public long Tokens => (long)InputTokens + OutputTokens;
}

internal sealed record PlugInUsage(DateTimeOffset Hour, string PlugInId, string PlugInName, string Vendor, string ModelId, string ModelName, long Tokens);

internal abstract record UsageSeries(long Total)
{
    public sealed record Named(string Name, SeriesColour Colour, long Total) : UsageSeries(Total);
    public sealed record Others(int Count, long Total) : UsageSeries(Total);
}

internal sealed record UsageBucket(DateTime Start, IReadOnlyList<long> Values)
{
    public long Total => Values.Sum();
}

internal sealed record UsageChartData(UsageGrain Grain, UsageSplit Split, IReadOnlyList<UsageSeries> Series, IReadOnlyList<UsageBucket> Buckets)
{
    public long Peak => Buckets.Count == 0 ? 0 : Buckets.Max(bucket => bucket.Total);

    public long Total => Series.Sum(series => series.Total);
}

internal sealed record UsageSummary(long InputTokens, long OutputTokens, int Turns, UsageChartData Chart);

internal sealed record PlugInBadge(string Id, string Name, long Total);

internal static class UsageReport
{
    public const int NamedSeries = 5;

    private const int DailyAllTimeLimit = 31;
    private const int WeeklyAllTimeLimit = 7 * 26;
    private const int ShortestDailySpan = 7;

    private readonly record struct Sample(DateTime At, string Id, string Name, string? Vendor, long Tokens);

    private readonly record struct RankedSeries(string Id, string Name, string? Vendor);

    private sealed record UsageWindow(UsageGrain Grain, DateTime Start, DateTime Today)
    {
        public bool Contains(DateTime at) => at >= Start && at < Today.AddDays(1);
    }

    public static UsageSummary Summarize(IReadOnlyList<UsageEntry> entries, UsageRange range, DateTimeOffset now, TimeZoneInfo zone, DayOfWeek firstDayOfWeek)
    {
        List<(UsageEntry Entry, Sample Sample)> local = entries
            .Select(entry => (Entry: entry, Sample: new Sample(Local(entry.At, zone), entry.AgentId, entry.AgentName, entry.Vendor, entry.Tokens)))
            .ToList();
        List<Sample> samples = local.Select(item => item.Sample).ToList();
        UsageWindow window = WindowFor(range, samples, now, zone, firstDayOfWeek);
        List<UsageEntry> inRange = local.Where(item => window.Contains(item.Sample.At)).Select(item => item.Entry).ToList();

        return new UsageSummary(
            inRange.Sum(entry => (long)entry.InputTokens),
            inRange.Sum(entry => (long)entry.OutputTokens),
            inRange.Count,
            Chart(UsageSplit.Agent, window, firstDayOfWeek, samples, samples));
    }

    public static IReadOnlyList<PlugInBadge> PlugInBadges(IReadOnlyList<PlugInUsage> usage, UsageRange range, DateTimeOffset now, TimeZoneInfo zone, DayOfWeek firstDayOfWeek)
    {
        List<Sample> samples = usage.Select(hour => new Sample(Local(hour.Hour, zone), hour.PlugInId, hour.PlugInName, null, hour.Tokens)).ToList();
        Dictionary<string, long> totals = TotalsIn(WindowFor(range, samples, now, zone, firstDayOfWeek), samples);
        return Ranked(samples)
            .Select(plugIn => new PlugInBadge(plugIn.Id, plugIn.Name, totals.TryGetValue(plugIn.Id, out long total) ? total : 0))
            .Where(badge => badge.Total > 0)
            .ToList();
    }

    public static UsageChartData PlugInChart(IReadOnlyList<PlugInUsage> usage, IReadOnlyCollection<string> plugInIds, UsageRange range, DateTimeOffset now, TimeZoneInfo zone, DayOfWeek firstDayOfWeek)
    {
        List<Sample> all = usage.Select(hour => ModelSample(hour, zone)).ToList();
        List<Sample> shown = usage.Where(hour => plugInIds.Contains(hour.PlugInId)).Select(hour => ModelSample(hour, zone)).ToList();
        return Chart(UsageSplit.Model, WindowFor(range, all, now, zone, firstDayOfWeek), firstDayOfWeek, all, shown);
    }

    private static Sample ModelSample(PlugInUsage hour, TimeZoneInfo zone) => new(Local(hour.Hour, zone), hour.ModelId, hour.ModelName, hour.Vendor, hour.Tokens);

    private static DateTime Local(DateTimeOffset at, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(at, zone).DateTime;

    private static UsageWindow WindowFor(UsageRange range, IReadOnlyList<Sample> samples, DateTimeOffset now, TimeZoneInfo zone, DayOfWeek firstDayOfWeek)
    {
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).Date;
        DateTime end = today.AddDays(1);
        (UsageGrain grain, DateTime start) = range switch
        {
            UsageRange.Today => (UsageGrain.Hour, today),
            UsageRange.Week => (UsageGrain.Day, today.AddDays(-6)),
            UsageRange.Month => (UsageGrain.Day, today.AddDays(-29)),
            UsageRange.AllTime => AllTime(samples.Where(sample => sample.At < end).Select(sample => sample.At.Date).DefaultIfEmpty(today).Min(), today, firstDayOfWeek),
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, null),
        };
        return new UsageWindow(grain, start, today);
    }

    private static UsageChartData Chart(UsageSplit split, UsageWindow window, DayOfWeek firstDayOfWeek, IReadOnlyList<Sample> all, IReadOnlyList<Sample> shown)
    {
        (List<UsageSeries> series, Dictionary<string, int> seriesOf) = SeriesFor(Ranked(all), TotalsIn(window, shown));

        List<DateTime> starts = BucketStarts(window.Grain, window.Start, window.Today, firstDayOfWeek);
        Dictionary<DateTime, long[]> values = starts.ToDictionary(bucket => bucket, _ => new long[series.Count]);
        foreach (Sample sample in shown)
        {
            if (window.Contains(sample.At) && seriesOf.TryGetValue(sample.Id, out int index) && values.TryGetValue(Floor(window.Grain, sample.At, firstDayOfWeek), out long[]? bucket))
                bucket[index] += sample.Tokens;
        }

        return new UsageChartData(window.Grain, split, series, starts.Select(bucket => new UsageBucket(bucket, values[bucket])).ToList());
    }

    private static Dictionary<string, long> TotalsIn(UsageWindow window, IReadOnlyList<Sample> samples) => samples
        .Where(sample => window.Contains(sample.At))
        .GroupBy(sample => sample.Id)
        .ToDictionary(group => group.Key, group => group.Sum(sample => sample.Tokens));

    private static List<RankedSeries> Ranked(IReadOnlyList<Sample> all) => all
        .GroupBy(sample => sample.Id)
        .Select(group => (Series: new RankedSeries(group.Key, group.First().Name, group.First().Vendor), Total: group.Sum(sample => sample.Tokens)))
        .OrderByDescending(item => item.Total)
        .ThenBy(item => item.Series.Name, StringComparer.CurrentCultureIgnoreCase)
        .Select(item => item.Series)
        .ToList();

    private static (List<UsageSeries> Series, Dictionary<string, int> SeriesOf) SeriesFor(IReadOnlyList<RankedSeries> ranked, IReadOnlyDictionary<string, long> totals)
    {
        List<UsageSeries> series = [];
        Dictionary<string, int> seriesOf = [];
        Dictionary<SeriesHue, int> shadesTaken = [];
        for (int rank = 0; rank < Math.Min(NamedSeries, ranked.Count); rank++)
        {
            RankedSeries ranking = ranked[rank];
            SeriesHue hue = SeriesHue.Of(ranking.Vendor, ranking.Id);
            int shade = shadesTaken.TryGetValue(hue, out int taken) ? taken : 0;
            shadesTaken[hue] = shade + 1;
            if (!totals.TryGetValue(ranking.Id, out long total) || total == 0)
                continue;

            seriesOf[ranking.Id] = series.Count;
            series.Add(new UsageSeries.Named(ranking.Name, new SeriesColour(hue, shade), total));
        }

        List<(string Id, long Total)> others = ranked.Skip(NamedSeries)
            .Select(item => (item.Id, Total: totals.TryGetValue(item.Id, out long total) ? total : 0))
            .Where(item => item.Total > 0)
            .ToList();
        if (others.Count > 0)
        {
            foreach ((string id, long _) in others)
                seriesOf[id] = series.Count;
            series.Add(new UsageSeries.Others(others.Count, others.Sum(item => item.Total)));
        }

        return (series, seriesOf);
    }

    private static (UsageGrain Grain, DateTime Start) AllTime(DateTime first, DateTime today, DayOfWeek firstDayOfWeek)
    {
        int days = (today - first).Days + 1;
        if (days <= DailyAllTimeLimit)
            return (UsageGrain.Day, days < ShortestDailySpan ? today.AddDays(1 - ShortestDailySpan) : first);
        if (days <= WeeklyAllTimeLimit)
            return (UsageGrain.Week, WeekStart(first, firstDayOfWeek));
        return (UsageGrain.Month, new DateTime(first.Year, first.Month, 1));
    }

    private static List<DateTime> BucketStarts(UsageGrain grain, DateTime start, DateTime today, DayOfWeek firstDayOfWeek)
    {
        DateTime last = grain == UsageGrain.Hour ? today.AddHours(23) : Floor(grain, today, firstDayOfWeek);
        List<DateTime> starts = [];
        for (DateTime bucket = start; bucket <= last; bucket = Next(grain, bucket))
            starts.Add(bucket);
        return starts;
    }

    private static DateTime Next(UsageGrain grain, DateTime bucket) => grain switch
    {
        UsageGrain.Hour => bucket.AddHours(1),
        UsageGrain.Day => bucket.AddDays(1),
        UsageGrain.Week => bucket.AddDays(7),
        UsageGrain.Month => bucket.AddMonths(1),
        _ => throw new ArgumentOutOfRangeException(nameof(grain), grain, null),
    };

    private static DateTime Floor(UsageGrain grain, DateTime at, DayOfWeek firstDayOfWeek) => grain switch
    {
        UsageGrain.Hour => new DateTime(at.Year, at.Month, at.Day, at.Hour, 0, 0),
        UsageGrain.Day => at.Date,
        UsageGrain.Week => WeekStart(at.Date, firstDayOfWeek),
        UsageGrain.Month => new DateTime(at.Year, at.Month, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(grain), grain, null),
    };

    private static DateTime WeekStart(DateTime date, DayOfWeek firstDayOfWeek)
        => date.AddDays(-(((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7));
}
