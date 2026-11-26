using System.Globalization;

using Eto.Drawing;

using Rhino.UI;

using Rhino.AI.Statistics;

namespace Rhino.AI.UI.Settings;

internal sealed class UsagePageViewModel : SimpleViewModel
{
    private sealed record UsageView(UsageSummary Agents, IReadOnlyList<PlugInBadge> Badges, IReadOnlyCollection<string> SelectedPlugIns, UsageChartData PlugInChart);

    private UsageRange range = UsageRange.Week;
    private UsageView view;

    private UsagePageViewModel(IReadOnlyList<UsageEntry> entries, IReadOnlyList<PlugInUsage> plugInUsage, IReadOnlyDictionary<string, Image> plugInIcons, Func<DateTimeOffset> clock)
    {
        Entries = entries;
        PlugInUsage = plugInUsage;
        Icons = plugInIcons;
        Clock = clock;
        view = View([]);
    }

    private IReadOnlyList<UsageEntry> Entries { get; }

    private IReadOnlyList<PlugInUsage> PlugInUsage { get; }

    private IReadOnlyDictionary<string, Image> Icons { get; }

    private Func<DateTimeOffset> Clock { get; }

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static UsagePageViewModel Load()
    {
        List<UsageEntry> entries = UsageHistory.Turns()
            .Select(turn => new UsageEntry(turn.At, turn.AgentId, turn.AgentName, turn.Vendor, turn.InputTokens, turn.OutputTokens))
            .ToList();

        IReadOnlyList<PlugInHourUsage> hours = PlugInStatistics.Hourly();
        List<PlugInUsage> plugInUsage = hours
            .Select(hour => new PlugInUsage(hour.Hour, hour.Id.ToString(), hour.Name, hour.Vendor, hour.Vendor + "/" + hour.Model, ModelName(hour.Vendor, hour.Model), hour.TokenCount))
            .ToList();

        Dictionary<string, Image> icons = [];
        foreach (Guid id in hours.Select(hour => hour.Id).Distinct())
        {
            if (PlugInIcons.Of(id) is Image icon)
                icons[id.ToString()] = icon;
        }

        return new UsagePageViewModel(entries, plugInUsage, icons, () => DateTimeOffset.Now);
    }

    public Image? IconOf(string plugInId) => Icons.TryGetValue(plugInId, out Image? icon) ? icon : null;

    public UsageRange Range
    {
        get => range;
        set
        {
            if (!SetProperty(ref range, value))
                return;

            Show(view.SelectedPlugIns);
        }
    }

    public IReadOnlyList<PlugInBadge> Badges => view.Badges;

    public IReadOnlyCollection<string> SelectedPlugIns => view.SelectedPlugIns;

    public string TotalToolTip => IsFiltered
        ? string.Format(LOC.STR("{0} tokens used by the selected plug-ins."), view.PlugInChart.Total.ToString("N0", Culture))
        : string.Format(
            LOC.STR("{0} input and {1} output tokens over {2} turns, counted from the conversations saved in the AI panel."),
            view.Agents.InputTokens.ToString("N0", Culture),
            view.Agents.OutputTokens.ToString("N0", Culture),
            view.Agents.Turns.ToString("N0", Culture));

    public UsageChartData Chart => IsFiltered ? view.PlugInChart : view.Agents.Chart;

    public string EmptyText => Entries.Count == 0
        ? LOC.STR("Usage appears after you chat with an agent in the AI panel.")
        : range switch
        {
            UsageRange.Today => LOC.STR("No usage today."),
            UsageRange.Week => LOC.STR("No usage in the last 7 days."),
            UsageRange.Month => LOC.STR("No usage in the last 30 days."),
            UsageRange.AllTime => LOC.STR("No usage yet."),
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, null),
        };

    private bool IsFiltered => view.SelectedPlugIns.Count > 0;

    public void Toggle(string plugInId)
        => Show(view.SelectedPlugIns.Contains(plugInId)
            ? view.SelectedPlugIns.Where(id => id != plugInId).ToList()
            : [.. view.SelectedPlugIns, plugInId]);

    private void Show(IReadOnlyCollection<string> wanted)
    {
        view = View(wanted);
        foreach (string property in new[] { nameof(Badges), nameof(SelectedPlugIns), nameof(TotalToolTip), nameof(Chart), nameof(EmptyText) })
            RaisePropertyChanged(property);
    }

    private static string ModelName(string vendor, string model)
        => AIInstances.Agents.Definitions
            .SelectMany(definition => definition.AvailableModels)
            .FirstOrDefault(known => string.Equals(known.ModelId, model, StringComparison.OrdinalIgnoreCase) && string.Equals(known.Vendor, vendor, StringComparison.OrdinalIgnoreCase))
            is ModelDefinition known ? known.DisplayName : model;

    private UsageView View(IReadOnlyCollection<string> wanted)
    {
        DateTimeOffset now = Clock();
        TimeZoneInfo zone = TimeZoneInfo.Local;
        DayOfWeek firstDayOfWeek = Culture.DateTimeFormat.FirstDayOfWeek;

        IReadOnlyList<PlugInBadge> badges = UsageReport.PlugInBadges(PlugInUsage, range, now, zone, firstDayOfWeek);
        List<string> selected = badges.Select(badge => badge.Id).Where(id => wanted.Contains(id)).ToList();
        return new UsageView(
            UsageReport.Summarize(Entries, range, now, zone, firstDayOfWeek),
            badges,
            selected,
            UsageReport.PlugInChart(PlugInUsage, selected, range, now, zone, firstDayOfWeek));
    }
}
