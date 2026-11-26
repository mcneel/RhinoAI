#if R9
using Eto.Drawing;

namespace Rhino.AI.UI.Settings;

internal static class Palette
{
    public static Color Allow { get; } = Color.FromArgb(0x30, 0xA4, 0x6C);

    public static Color Danger { get; } = Color.FromArgb(0xE5, 0x48, 0x4D);

    public static Color Warning { get; } = Color.FromArgb(0xC2, 0x7A, 0x00);

    public static Color Secondary => new(SystemColors.ControlText, 0.6f);

    public static Color Rule => new(SystemColors.ControlText, 0.12f);

    public static Color SeriesOther { get; } = Color.FromArgb(0x8E, 0x8E, 0x93);

    private static Color[] SeriesColours { get; } =
    [
        Color.FromArgb(0x3E, 0x7B, 0xFA),
        Color.FromArgb(0xF0, 0x7F, 0x3C),
        Color.FromArgb(0x22, 0xA8, 0x84),
        Color.FromArgb(0xE3, 0xB3, 0x41),
        Color.FromArgb(0x9B, 0x6B, 0xF2),
    ];

    public static Color Series(int index) => SeriesColours[index % SeriesColours.Length];
}
#endif
