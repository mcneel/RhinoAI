using System.Globalization;

namespace RhinoAI.WebPanel;

// Rhino's own chrome colours, translated into the panel's CSS custom properties.
//
// Rhino themes Eto, so Eto's SystemColors are Rhino's panel colours: reading them is what makes the
// web panel sit in a Rhino window without looking pasted on. Only the chrome is taken over. The
// semantic colours (ok / warn / error) stay with the panel's own stylesheet, because "this tool
// failed" should not change meaning with the host's accent.
//
// Everything is derived from four inputs so a theme we have never seen still produces a coherent
// set, rather than a background that matches and borders that do not. The maths is deliberately
// separate from the Eto lookup so it can be tested without a UI thread.
internal static class PanelTheme
{
    internal readonly record struct Rgb(float R, float G, float B)
    {
        public float Luminance => (0.2126f * R) + (0.7152f * G) + (0.0722f * B);
    }

    // Named so the mapping is reviewable. Most of these come from Rhino's own paint palette rather
    // than from Eto's system colours, because that palette is what Rhino paints its panels with.
    internal readonly record struct Palette(
        Rgb Chrome,
        Rgb Field,
        Rgb Text,
        Rgb Dim,
        Rgb Border,
        Rgb Accent,
        Rgb AccentText,
        Rgb Link,
        Rgb Selection,
        Rgb SelectionText);

    public static bool IsDark(Rgb background) => background.Luminance < 0.5f;

    // WCAG relative luminance and contrast ratio. Worth the few lines: a palette entry is only
    // useful if text can be read on it, and Rhino's entries cannot all be trusted to follow the
    // theme (EditBoxBackground comes back white in dark mode on macOS, which put white text on a
    // white ground). Anything that fails this is replaced rather than rendered.
    private static float Linear(float channel) =>
        channel <= 0.03928f ? channel / 12.92f : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);

    private static float RelativeLuminance(Rgb colour) =>
        (0.2126f * Linear(colour.R)) + (0.7152f * Linear(colour.G)) + (0.0722f * Linear(colour.B));

    internal static float Contrast(Rgb a, Rgb b)
    {
        float first = RelativeLuminance(a);
        float second = RelativeLuminance(b);
        return (MathF.Max(first, second) + 0.05f) / (MathF.Min(first, second) + 0.05f);
    }

    // Large areas of UI, so 3:1 rather than the 4.5:1 wanted for body text at small sizes.
    private const float MinimumContrast = 3f;

    /// <summary>
    /// The colour the transcript sits on. The entry background is preferred, because that is what
    /// Rhino puts lists and text boxes on, but only when text is actually legible against it;
    /// otherwise the panel background, and failing that a plain ground derived from the text.
    /// </summary>
    internal static Rgb ContentGround(Palette palette)
    {
        if (Contrast(palette.Field, palette.Text) >= MinimumContrast)
            return palette.Field;
        if (Contrast(palette.Chrome, palette.Text) >= MinimumContrast)
            return palette.Chrome;
        return IsDark(palette.Text) ? new Rgb(1f, 1f, 1f) : new Rgb(0.11f, 0.11f, 0.12f);
    }

    /// <summary>The chrome bars, held to the same standard as the content area.</summary>
    internal static Rgb ChromeGround(Palette palette) =>
        Contrast(palette.Chrome, palette.Text) >= MinimumContrast ? palette.Chrome : ContentGround(palette);

    /// <summary>Whether the resolved theme reads as dark, which is what the panel's scheme follows.</summary>
    public static bool IsDarkTheme(Palette palette) => IsDark(ContentGround(palette));

    /// <summary>
    /// CSS custom property names (without the leading --) mapped to colour literals, ready for the
    /// panel's `theme` event.
    /// </summary>
    public static Dictionary<string, string> Tokens(Palette palette)
    {
        // The transcript is the panel's content area, so it takes the grid / edit-box tone and the
        // whole surface ramp is derived from it. The chrome bars keep the panel colour and get their
        // own hover, or a header button would light up in a shade meant for a card on the content.
        // Both go through the contrast check above, so an untrustworthy entry cannot make the panel
        // unreadable.
        Rgb background = ContentGround(palette);
        Rgb chrome = ChromeGround(palette);
        Rgb text = palette.Text;
        Rgb accent = palette.Accent;
        bool dark = IsDark(background);

        // A card has to be perceptibly off the ground it sits on. Lightening only achieves that when
        // there is headroom, and the light content ground is usually pure white, so cards there step
        // toward the text instead.
        Rgb surface = dark ? Lighten(background, 0.05f) : Mix(background, text, 0.035f);
        Rgb toward(float amount) => Mix(background, text, amount);

        return new Dictionary<string, string>
        {
            ["bg"] = Hex(background),
            ["surface"] = Hex(surface),
            ["surface-2"] = Hex(toward(dark ? 0.07f : 0.05f)),
            ["surface-3"] = Hex(toward(dark ? 0.12f : 0.10f)),

            // Rhino draws its own separators on a panel background, so the border and the rule are
            // taken from it rather than mixed by eye. The strong variant is the only derived one.
            ["border"] = Hex(palette.Border),
            ["rule"] = Hex(palette.Border),
            ["border-strong"] = Hex(Mix(palette.Border, text, 0.35f)),

            // The chrome bars are the panel's own colour, separated from the content by Rhino's own
            // grid line, exactly as in a Rhino panel.
            ["control"] = Hex(chrome),
            ["control-hover"] = Hex(Mix(chrome, text, dark ? 0.10f : 0.08f)),

            // Where the user types. The same tone as the content area, which is the point.
            ["field"] = Hex(background),

            ["text"] = Hex(text),
            ["text-dim"] = Hex(palette.Dim),
            ["text-faint"] = Hex(Mix(palette.Dim, background, 0.35f)),

            ["accent"] = Hex(accent),
            ["accent-text"] = Hex(palette.AccentText),
            ["link"] = Hex(palette.Link),
            ["selection"] = Hex(palette.Selection),
            ["selection-text"] = Hex(palette.SelectionText),
            ["accent-soft"] = Hex(Mix(background, accent, dark ? 0.22f : 0.13f)),
            ["accent-line"] = Hex(Mix(background, accent, dark ? 0.45f : 0.35f)),

            // These two are composited over content, so they need real alpha rather than a blend.
            ["accent-ring"] = Rgba(accent, 0.20f),
            ["bg-fade"] = Rgba(background, 0.86f),
        };
    }

    // The stylesheet's own stack, kept as the tail so a family Rhino names but the webview cannot
    // resolve still lands on something sensible.
    private const string FontFallback =
        "-apple-system, BlinkMacSystemFont, \"Segoe UI Variable Text\", \"Segoe UI\", system-ui, sans-serif";

    // Ratios that reproduce the stylesheet's own 10.5 / 11.5 / 12.5 / 13.5 at a 13px base.
    private static readonly (string Token, float Ratio)[] Sizes =
    [
        ("fs-xs", 0.808f),
        ("fs-sm", 0.885f),
        ("fs", 0.962f),
        ("fs-md", 1.038f),
    ];

    /// <summary>
    /// Font tokens taken from Rhino's own UI font, so the panel reads as part of Rhino rather than
    /// as a web page inside it.
    /// </summary>
    /// <param name="pointSize">Eto reports point sizes; see the conversion below.</param>
    public static Dictionary<string, string> Fonts(string familyName, float pointSize, bool windows)
    {
        string family = SanitizeFamily(familyName);

        // A webview lays out in CSS pixels. On Windows those are 1/96 inch against Eto's 1/72 inch
        // points; on macOS a point and a CSS pixel are the same logical unit.
        float px = windows ? pointSize * 96f / 72f : pointSize;

        // An implausible size means something is off in the conversion or the theme, and a panel
        // rendered at 4px or 40px is worse than one that ignores the host, so keep our own baseline.
        if (px is not (>= 8f and <= 24f))
            px = 13f;

        Dictionary<string, string> tokens = new()
        {
            ["font"] = family.Length > 0 ? $"\"{family}\", {FontFallback}" : FontFallback,
        };
        foreach ((string token, float ratio) in Sizes)
            tokens[token] = $"{(px * ratio).ToString("0.##", CultureInfo.InvariantCulture)}px";
        return tokens;
    }

    // The family name reaches the page as a CSS value, so it is reduced to the characters a font
    // family can legitimately contain rather than trusted verbatim.
    private static string SanitizeFamily(string familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName))
            return string.Empty;
        System.Text.StringBuilder clean = new();
        foreach (char c in familyName.Trim())
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.')
                clean.Append(c);
        return clean.ToString().Trim();
    }

    private static Rgb Mix(Rgb from, Rgb to, float amount)
    {
        float t = Math.Clamp(amount, 0f, 1f);
        return new Rgb(
            from.R + ((to.R - from.R) * t),
            from.G + ((to.G - from.G) * t),
            from.B + ((to.B - from.B) * t));
    }

    private static Rgb Lighten(Rgb colour, float amount) => Mix(colour, new Rgb(1f, 1f, 1f), amount);

    private static string Hex(Rgb colour) =>
        $"#{Channel(colour.R):X2}{Channel(colour.G):X2}{Channel(colour.B):X2}";

    private static string Rgba(Rgb colour, float alpha) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "rgba({0}, {1}, {2}, {3:0.##})",
            Channel(colour.R),
            Channel(colour.G),
            Channel(colour.B),
            Math.Clamp(alpha, 0f, 1f));

    private static int Channel(float value) => (int)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
