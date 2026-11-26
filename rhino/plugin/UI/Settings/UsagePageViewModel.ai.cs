#if R9
using System.Globalization;

using Rhino.UI;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class UsagePageViewModel : SimpleViewModel
{
    private UsageRange range = UsageRange.Week;
    private UsageSummary summary;

    private UsagePageViewModel(IReadOnlyList<UsageEntry> entries, Func<DateTimeOffset> clock)
    {
        Entries = entries;
        Clock = clock;
        summary = Summarize();
    }

    private IReadOnlyList<UsageEntry> Entries { get; }

    private Func<DateTimeOffset> Clock { get; }

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static UsagePageViewModel Load()
    {
        List<UsageEntry> entries = UsageHistory.Turns()
            .Select(turn => new UsageEntry(turn.At, turn.AgentId, turn.AgentName, turn.InputTokens, turn.OutputTokens, turn.CostUsd))
            .ToList();
        return new UsagePageViewModel(entries, () => DateTimeOffset.Now);
    }

    public UsageRange Range
    {
        get => range;
        set
        {
            if (!SetProperty(ref range, value))
                return;

            summary = Summarize();
            foreach (string property in new[] { nameof(TokensText), nameof(TokensCaption), nameof(TokensToolTip), nameof(CostText), nameof(CostCaption), nameof(CostToolTip), nameof(Chart), nameof(EmptyText) })
                RaisePropertyChanged(property);
        }
    }

    public string TokensText => UsageFormat.Compact(summary.Tokens, Culture);

    public string TokensCaption => summary.Tokens == 1 ? LOC.STR("token") : LOC.STR("tokens");

    public string TokensToolTip => string.Format(
        LOC.STR("{0} input and {1} output tokens over {2} turns, counted from the conversations saved in the AI panel."),
        summary.InputTokens.ToString("N0", Culture),
        summary.OutputTokens.ToString("N0", Culture),
        summary.Turns.ToString("N0", Culture));

    public string CostText => IsCostUnreported ? LOC.STR("Cost") : UsageFormat.Cost(summary.Cost, Culture);

    public string CostCaption => IsCostUnreported ? LOC.STR("not reported") : LOC.STR("cost");

    public string CostToolTip => (summary.Turns - summary.CostedTurns) switch
    {
        0 => LOC.STR("The cost the agents reported for these turns."),
        int missing => string.Format(LOC.STR("Leaves out {0} of {1} turns, because their agent did not report a cost."), missing, summary.Turns),
    };

    public UsageChartData Chart => summary.Chart;

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

    private bool IsCostUnreported => summary.Tokens > 0 && summary.CostedTurns == 0;

    private UsageSummary Summarize()
        => UsageReport.Summarize(Entries, range, Clock(), TimeZoneInfo.Local, Culture.DateTimeFormat.FirstDayOfWeek);
}
#endif
