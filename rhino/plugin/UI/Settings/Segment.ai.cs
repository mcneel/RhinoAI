#if R9
using Eto.Drawing;

namespace Rhino.AI.UI.Settings;

internal enum SegmentIcon { None, Check, Question, Block }

internal abstract record SegmentStyle
{
    private SegmentStyle() { }

    public sealed record Neutral : SegmentStyle;
    public sealed record Tinted(Color Accent, Color OutlineInk) : SegmentStyle;
}

internal sealed record Segment<T>(T Value, string Label, SegmentIcon Icon, SegmentStyle Style)
    where T : struct, Enum;
#endif
