#if R9
using Eto.Drawing;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal static class UsageRanges
{
    public static IReadOnlyList<Segment<UsageRange>> Segments { get; } =
    [
        Segment(UsageRange.Today, LOC.STR("Today")),
        Segment(UsageRange.Week, LOC.STR("Week")),
        Segment(UsageRange.Month, LOC.STR("Month")),
        Segment(UsageRange.AllTime, LOC.STR("All time")),
    ];

    public static SegmentedPicker<UsageRange> Picker() => new(Segments, UsageRange.Week)
    {
        ToolTip = LOC.STR("Week and Month count back 7 and 30 days, including today."),
    };

    private static Segment<UsageRange> Segment(UsageRange range, string label)
        => new(range, label, SegmentIcon.None, new SegmentStyle.Neutral());
}
#endif
