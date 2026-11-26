#if R9
namespace Rhino.AI.UI.Settings;

internal enum UsageRange { Today, Week, Month, AllTime }

internal enum UsageGrain { Hour, Day, Week, Month }

internal sealed record UsageEntry(DateTimeOffset At, string AgentId, string AgentName, int InputTokens, int OutputTokens, decimal? CostUsd)
{
    public long Tokens => (long)InputTokens + OutputTokens;
}

internal abstract record UsageSeries(long Total)
{
    public sealed record Agent(string Name, int Colour, long Total) : UsageSeries(Total);
    public sealed record Others(int Agents, long Total) : UsageSeries(Total);
}

internal sealed record UsageBucket(DateTime Start, IReadOnlyList<long> Values)
{
    public long Total => Values.Sum();
}

internal sealed record UsageChartData(UsageGrain Grain, IReadOnlyList<UsageSeries> Series, IReadOnlyList<UsageBucket> Buckets)
{
    public long Peak => Buckets.Count == 0 ? 0 : Buckets.Max(bucket => bucket.Total);
}

internal sealed record UsageSummary(long InputTokens, long OutputTokens, decimal Cost, int Turns, int CostedTurns, UsageChartData Chart)
{
    public long Tokens => InputTokens + OutputTokens;
}

internal static class UsageReport
{
    public const int NamedSeries = 5;

    private const int DailyAllTimeLimit = 31;
    private const int WeeklyAllTimeLimit = 7 * 26;
    private const int ShortestDailySpan = 7;

    public static UsageSummary Summarize(IReadOnlyList<UsageEntry> entries, UsageRange range, DateTimeOffset now, TimeZoneInfo zone, DayOfWeek firstDayOfWeek)
    {
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).Date;
        DateTime end = today.AddDays(1);
        List<(UsageEntry Entry, DateTime At)> local = entries
            .Select(entry => (Entry: entry, At: TimeZoneInfo.ConvertTime(entry.At, zone).DateTime))
            .Where(item => item.At < end)
            .ToList();

        (UsageGrain grain, DateTime start) = range switch
        {
            UsageRange.Today => (UsageGrain.Hour, today),
            UsageRange.Week => (UsageGrain.Day, today.AddDays(-6)),
            UsageRange.Month => (UsageGrain.Day, today.AddDays(-29)),
            UsageRange.AllTime => AllTime(local.Select(item => item.At.Date).DefaultIfEmpty(today).Min(), today, firstDayOfWeek),
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, null),
        };

        List<(UsageEntry Entry, DateTime At)> inRange = local.Where(item => item.At >= start).ToList();
        (List<UsageSeries> series, Dictionary<string, int> seriesOf) = SeriesFor(entries, inRange.Select(item => item.Entry).ToList());

        List<DateTime> starts = BucketStarts(grain, start, today, firstDayOfWeek);
        Dictionary<DateTime, long[]> values = starts.ToDictionary(bucket => bucket, _ => new long[series.Count]);
        foreach ((UsageEntry entry, DateTime at) in inRange)
        {
            if (seriesOf.TryGetValue(entry.AgentId, out int index) && values.TryGetValue(Floor(grain, at, firstDayOfWeek), out long[]? bucket))
                bucket[index] += entry.Tokens;
        }

        UsageChartData chart = new(grain, series, starts.Select(bucket => new UsageBucket(bucket, values[bucket])).ToList());

        return new UsageSummary(
            inRange.Sum(item => (long)item.Entry.InputTokens),
            inRange.Sum(item => (long)item.Entry.OutputTokens),
            inRange.Select(item => item.Entry.CostUsd).OfType<decimal>().Sum(),
            inRange.Count,
            inRange.Count(item => item.Entry.CostUsd is not null),
            chart);
    }

    // Colours follow each agent's rank across all saved usage, so an agent keeps its colour when the range changes.
    private static (List<UsageSeries> Series, Dictionary<string, int> SeriesOf) SeriesFor(IReadOnlyList<UsageEntry> all, IReadOnlyList<UsageEntry> inRange)
    {
        List<(string Id, string Name)> ranked = all
            .GroupBy(entry => entry.AgentId)
            .Select(group => (Id: group.Key, Name: group.First().AgentName, Total: group.Sum(entry => entry.Tokens)))
            .OrderByDescending(agent => agent.Total)
            .ThenBy(agent => agent.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(agent => (agent.Id, agent.Name))
            .ToList();

        Dictionary<string, long> totals = inRange
            .GroupBy(entry => entry.AgentId)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Tokens));

        List<UsageSeries> series = [];
        Dictionary<string, int> seriesOf = [];
        for (int rank = 0; rank < Math.Min(NamedSeries, ranked.Count); rank++)
        {
            (string id, string name) = ranked[rank];
            if (!totals.TryGetValue(id, out long total) || total == 0)
                continue;

            seriesOf[id] = series.Count;
            series.Add(new UsageSeries.Agent(name, rank, total));
        }

        List<(string Id, long Total)> others = ranked.Skip(NamedSeries)
            .Select(agent => (agent.Id, Total: totals.TryGetValue(agent.Id, out long total) ? total : 0))
            .Where(agent => agent.Total > 0)
            .ToList();
        if (others.Count > 0)
        {
            foreach ((string id, long _) in others)
                seriesOf[id] = series.Count;
            series.Add(new UsageSeries.Others(others.Count, others.Sum(agent => agent.Total)));
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
#endif
