using RhinoAI.WebPanel;

using Rgb = RhinoAI.WebPanel.PanelTheme.Rgb;

namespace RhinoAI.StreamJson.Tests;

// The panel takes its chrome from whatever Rhino's Eto theme reports, including themes that did not
// exist when this was written, so the derivation has to stay coherent for any input rather than be
// tuned for the two we happen to ship.
[TestFixture]
public sealed class PanelThemeTests
{
    private static readonly Rgb DarkBg = new(0.11f, 0.11f, 0.13f);
    private static readonly Rgb DarkText = new(0.91f, 0.92f, 0.93f);
    private static readonly Rgb LightBg = new(0.96f, 0.96f, 0.97f);
    private static readonly Rgb LightText = new(0.11f, 0.11f, 0.12f);
    private static readonly Rgb Dim = new(0.55f, 0.55f, 0.58f);
    private static readonly Rgb Accent = new(0.12f, 0.42f, 0.84f);
    private static readonly Rgb White = new(1f, 1f, 1f);
    private static readonly Rgb Field = new(0.08f, 0.08f, 0.09f);
    private static readonly Rgb LightField = new(1f, 1f, 1f);
    private static readonly Rgb Link = new(0.44f, 0.66f, 1f);
    private static readonly Rgb Selection = new(0.18f, 0.29f, 0.46f);
    private static readonly Rgb Border = new(0.30f, 0.30f, 0.32f);

    // The entry background has to suit the text, or the contrast guard rightly rejects it and the
    // fixture stops describing a theme Rhino could actually produce.
    private static PanelTheme.Palette Palette(Rgb window, Rgb text) =>
        new(window, PanelTheme.IsDark(text) ? LightField : Field, text, Dim, Border, Accent, White, Link, Selection, White);

    private static Dictionary<string, string> Dark() => PanelTheme.Tokens(Palette(DarkBg, DarkText));
    private static Dictionary<string, string> Light() => PanelTheme.Tokens(Palette(LightBg, LightText));

    private static float Luminance(string hex)
    {
        int value = Convert.ToInt32(hex.TrimStart('#'), 16);
        return ((0.2126f * ((value >> 16) & 0xFF)) + (0.7152f * ((value >> 8) & 0xFF)) + (0.0722f * (value & 0xFF))) / 255f;
    }

    // Rhino's palette entries do not all follow the theme: EditBoxBackground comes back white in
    // dark mode on macOS, which rendered white text on a white ground. Any entry that fails contrast
    // has to be replaced rather than trusted, whatever it is nominally for.
    [Test]
    public void An_entry_background_that_text_cannot_be_read_on_is_rejected()
    {
        Rgb white = new(1f, 1f, 1f);
        Rgb lightText = new(0.91f, 0.92f, 0.93f);
        Rgb darkPanel = new(0.11f, 0.11f, 0.13f);

        PanelTheme.Palette broken =
            new(darkPanel, white, lightText, Dim, Border, Accent, White, Link, Selection, White);
        Dictionary<string, string> tokens = PanelTheme.Tokens(broken);

        Assert.That(PanelTheme.Contrast(white, lightText), Is.LessThan(3f), "the premise: white on white");
        Assert.That(tokens["bg"], Is.Not.EqualTo("#FFFFFF"), "the unusable entry must not be rendered");
        Assert.That(PanelTheme.Contrast(Parse(tokens["bg"]), lightText), Is.GreaterThanOrEqualTo(3f));
        Assert.That(PanelTheme.Contrast(Parse(tokens["control"]), lightText), Is.GreaterThanOrEqualTo(3f));
        Assert.That(PanelTheme.Contrast(Parse(tokens["field"]), lightText), Is.GreaterThanOrEqualTo(3f));
    }

    [Test]
    public void A_theme_where_nothing_contrasts_still_produces_a_readable_panel()
    {
        // Both grounds unusable: the ground is derived from the text rather than giving up.
        Rgb white = new(1f, 1f, 1f);
        Rgb lightText = new(0.95f, 0.95f, 0.95f);
        PanelTheme.Palette hopeless =
            new(white, white, lightText, Dim, Border, Accent, White, Link, Selection, White);

        Dictionary<string, string> tokens = PanelTheme.Tokens(hopeless);
        Assert.That(PanelTheme.Contrast(Parse(tokens["bg"]), lightText), Is.GreaterThanOrEqualTo(3f));
    }

    [Test]
    public void The_scheme_follows_the_ground_that_was_actually_rendered()
    {
        // A light entry background rejected in favour of a dark panel must report as dark, or the
        // stylesheet's own light/dark tokens fight the ones the host sent.
        Rgb white = new(1f, 1f, 1f);
        Rgb lightText = new(0.91f, 0.92f, 0.93f);
        Rgb darkPanel = new(0.11f, 0.11f, 0.13f);
        PanelTheme.Palette broken =
            new(darkPanel, white, lightText, Dim, Border, Accent, White, Link, Selection, White);

        Assert.That(PanelTheme.IsDarkTheme(broken), Is.True);
    }

    private static Rgb Parse(string hex)
    {
        int value = System.Convert.ToInt32(hex.TrimStart('#'), 16);
        return new Rgb(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
    }

    [Test]
    public void Dark_and_light_backgrounds_are_told_apart()
    {
        Assert.That(PanelTheme.IsDark(DarkBg), Is.True);
        Assert.That(PanelTheme.IsDark(LightBg), Is.False);
    }

    [Test]
    public void Both_grounds_are_passed_through_untouched()
    {
        // The chrome is Rhino's PanelBackground and the content area its EditBoxBackground; neither
        // is derived, so neither should be altered on the way through.
        Assert.That(Dark()["control"], Is.EqualTo("#1C1C21"));
        Assert.That(Light()["control"], Is.EqualTo("#F5F5F7"));
        Assert.That(Dark()["bg"], Is.EqualTo("#141417"));
    }

    [Test]
    public void Surfaces_are_perceptibly_off_the_background_in_both_themes()
    {
        // Direction differs: a dark ground has headroom to lighten, a white one does not, so the
        // only thing worth asserting is that a card never disappears into the ground.
        Assert.That(Luminance(Dark()["surface"]), Is.GreaterThan(Luminance(Dark()["bg"])));
        Assert.That(Luminance(Light()["surface"]), Is.Not.EqualTo(Luminance(Light()["bg"])));
        Assert.That(Light()["surface"], Is.Not.EqualTo(Light()["bg"]));
    }

    [Test]
    public void Borders_come_from_rhinos_own_separator_colour_rather_than_being_mixed()
    {
        // GridLinesOnPanelBackground is what Rhino draws separators with, so it is used verbatim.
        Assert.That(Dark()["border"], Is.EqualTo("#4C4C52"));
        Assert.That(Dark()["rule"], Is.EqualTo(Dark()["border"]));
        Assert.That(Light()["border"], Is.EqualTo(Dark()["border"]),
            "the same source colour, so the same value whichever way round the theme is");
    }

    [Test]
    public void Only_the_strong_border_is_derived_and_it_leans_toward_the_text()
    {
        Dictionary<string, string> dark = Dark();
        Assert.That(Luminance(dark["border-strong"]), Is.GreaterThan(Luminance(dark["border"])),
            "light text on a dark ground");

        Dictionary<string, string> light = Light();
        Assert.That(Luminance(light["border-strong"]), Is.LessThan(Luminance(light["border"])),
            "dark text on a light ground");
    }

    [Test]
    public void Faint_text_sits_between_dim_text_and_the_background()
    {
        Dictionary<string, string> dark = Dark();
        float background = Luminance(dark["bg"]);
        float faint = Luminance(dark["text-faint"]);
        float dim = Luminance(dark["text-dim"]);
        Assert.That(faint, Is.LessThan(dim).And.GreaterThan(background));
    }

    [Test]
    public void Composited_tokens_carry_real_alpha_rather_than_a_blend()
    {
        Assert.That(Dark()["accent-ring"], Does.StartWith("rgba(").And.EndsWith("0.2)"));
        Assert.That(Dark()["bg-fade"], Does.StartWith("rgba(").And.EndsWith("0.86)"));
    }

    [Test]
    public void Every_token_the_stylesheet_expects_is_produced()
    {
        string[] required =
        [
            "bg", "surface", "surface-2", "surface-3", "border", "border-strong", "rule", "control", "control-hover", "field",
            "text", "text-dim", "text-faint",
            "accent", "accent-text", "accent-soft", "accent-line", "accent-ring", "bg-fade",
            "link", "selection", "selection-text",
        ];
        Dictionary<string, string> tokens = Dark();
        Assert.That(tokens.Keys, Is.EquivalentTo(required));
        foreach (string name in required)
            Assert.That(tokens[name], Is.Not.Empty, name);
    }

    [Test]
    public void The_chrome_and_the_content_area_are_different_tones()
    {
        // Rhino's own layout: grey bars, content on the grid / edit-box colour.
        Dictionary<string, string> tokens = Light();
        Assert.That(tokens["control"], Is.Not.EqualTo(tokens["bg"]));
        Assert.That(tokens["bg"], Is.EqualTo(tokens["field"]), "the transcript is a content area");
    }

    [Test]
    public void The_chrome_has_its_own_hover_rather_than_borrowing_the_contents()
    {
        // A header button lighting up in a shade mixed for a card on white would read as a blob.
        Dictionary<string, string> tokens = Light();
        Assert.That(tokens["control-hover"], Is.Not.EqualTo(tokens["control"]));
        Assert.That(tokens["control-hover"], Is.Not.EqualTo(tokens["surface-2"]));
    }

    [Test]
    public void The_entry_area_matches_the_content_area_not_the_chrome()
    {
        // The composer is a text box sitting in the chrome, so it takes the content tone, which is
        // what makes it read as an input rather than as part of the bar.
        Assert.That(Dark()["field"], Is.EqualTo(Dark()["bg"]));
        Assert.That(Dark()["field"], Is.Not.EqualTo(Dark()["control"]));
    }

    [Test]
    public void Semantic_colours_are_left_to_the_panel()
    {
        // "This tool failed" must not turn into the host's accent hue.
        Assert.That(Dark().Keys, Has.No.Member("err").And.No.Member("ok").And.No.Member("warn"));
    }

    [Test]
    public void Out_of_range_channels_clamp_instead_of_producing_broken_css()
    {
        Dictionary<string, string> tokens = PanelTheme.Tokens(
            Palette(new Rgb(-1f, 2f, 0.5f), new Rgb(2f, -1f, 0.5f)));

        foreach (string value in tokens.Values)
            Assert.That(value, Does.Match(@"^(#[0-9A-F]{6}|rgba\(\d{1,3}, \d{1,3}, \d{1,3}, [\d.]+\))$"), value);
    }
}
