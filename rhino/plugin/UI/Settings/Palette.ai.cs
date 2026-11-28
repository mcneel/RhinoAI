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

    private static Color[] Pool { get; } =
    [
        Color.FromArgb(0xB3, 0x20, 0x3A),
        Color.FromArgb(0xB7, 0xCB, 0x18),
        Color.FromArgb(0x63, 0x61, 0x09),
        Color.FromArgb(0x61, 0xCB, 0xFD),
        Color.FromArgb(0x7E, 0x42, 0xA7),
        Color.FromArgb(0x03, 0x6C, 0x92),
        Color.FromArgb(0xED, 0xA4, 0xCC),
        Color.FromArgb(0x12, 0xDE, 0xAA),
        Color.FromArgb(0xA8, 0x60, 0x20),
        Color.FromArgb(0xAD, 0xA4, 0xF9),
        Color.FromArgb(0xB1, 0x20, 0x83),
        Color.FromArgb(0x13, 0x7F, 0x1F),
        Color.FromArgb(0xDB, 0x38, 0x67),
        Color.FromArgb(0x8C, 0x4A, 0x6D),
        Color.FromArgb(0x5D, 0xCF, 0x4B),
        Color.FromArgb(0xB4, 0xB7, 0x6A),
        Color.FromArgb(0x03, 0x6F, 0x4F),
        Color.FromArgb(0x5B, 0x5B, 0x9A),
        Color.FromArgb(0x7C, 0x79, 0x03),
        Color.FromArgb(0x1C, 0xB3, 0xEC),
    ];

    // Alternating lighter and darker keeps a vendor's neighbouring series far apart; the most used keeps the vendor colour.
    private static float[] ShadeSteps { get; } = [0f, 0.35f, -0.3f, 0.6f, -0.5f];

    public static Color Series(SeriesColour colour) => Shade(Hue(colour.Hue), ShadeSteps[colour.Shade]);

    private static Color Hue(SeriesHue hue) => hue switch
    {
        SeriesHue.OfBrand brand => brand.Brand switch
        {
            Brand.DeepSeek => Color.FromArgb(0x3E, 0x7B, 0xFA),
            Brand.Anthropic => Color.FromArgb(0xF0, 0x7F, 0x3C),
            Brand.OpenAI => Color.FromArgb(0x22, 0xA8, 0x84),
            Brand.Google => Color.FromArgb(0xD5, 0x5F, 0xD0),
            _ => throw new ArgumentOutOfRangeException(nameof(hue), brand.Brand, null),
        },
        SeriesHue.Pooled pooled => Pool[pooled.Slot],
        _ => throw new InvalidOperationException("Unknown series hue."),
    };

    private static Color Shade(Color colour, float step) => step >= 0f
        ? new Color(colour.R + (1f - colour.R) * step, colour.G + (1f - colour.G) * step, colour.B + (1f - colour.B) * step)
        : new Color(colour.R * (1f + step), colour.G * (1f + step), colour.B * (1f + step));
}
