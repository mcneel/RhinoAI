#if R9
using Eto.Drawing;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal static class PermissionChoices
{
    public static IReadOnlyList<Segment<Permissability>> Segments { get; } =
    [
        new(Permissability.Always, Label(Permissability.Always), SegmentIcon.Check, new SegmentStyle.Tinted(Palette.Allow, Color.FromArgb(0x1F, 0x7A, 0x4D))),
        new(Permissability.Ask, Label(Permissability.Ask), SegmentIcon.Question, new SegmentStyle.Tinted(Palette.Warning, Color.FromArgb(0x9E, 0x62, 0x00))),
        new(Permissability.Deny, Label(Permissability.Deny), SegmentIcon.Block, new SegmentStyle.Tinted(Palette.Danger, Color.FromArgb(0xC4, 0x28, 0x2D))),
    ];

    public static int PickerWidth { get; } = SegmentedPicker<Permissability>.PreferredWidth(Segments);

    public static SegmentedPicker<Permissability> Picker() => new(Segments, Permissability.Unset)
    {
        ToolTip = LOC.STR("Allow runs the tool without asking. Ask checks with you each time. Block never runs it."),
    };

    public static string Label(Permissability choice) => choice switch
    {
        Permissability.Always => LOC.STR("Allow"),
        Permissability.Ask => LOC.STR("Ask"),
        Permissability.Deny => LOC.STR("Block"),
        Permissability.Unset => throw new ArgumentOutOfRangeException(nameof(choice), choice, "Unset has no label."),
        _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, null),
    };
}
#endif
