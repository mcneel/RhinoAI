using System.IO;
using System.Reflection;
using System.Drawing;
using Rhino.UI;
using Rhino.Runtime;

namespace RhMcp;

internal sealed class AIOptionsPage : OptionsDialogPage
{
    private const string IconResourceName = "RhMcp.logo.svg";

    private AISettingsPanel Panel { get; } = new();
    private Image? LightCachedImage { get; set; }
    private Image? DarkCachedImage { get; set; }

    public AIOptionsPage() : base("AI")
    {
        Panel.Width = 800;
    }

    public override object PageControl => Panel;

    // Mac's Settings UI lists pages by icon; a page with no PageImage never shows in the navigation.
    public override Image PageImage => HostUtils.RunningInDarkMode switch
    {
        true => DarkCachedImage ??= LoadIcon(true),
        _ => LightCachedImage ??= LoadIcon(false),
    };

    public override bool OnApply() => Panel.TryCommit(out _);

    public override bool OnActivate(bool active)
    {
        if (active)
            Modified = true;
        return base.OnActivate(active);
    }

    private static Bitmap LoadIcon(bool darkMode)
    {
        Assembly assembly = typeof(AIOptionsPage).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(IconResourceName)
            ?? throw new InvalidOperationException($"Embedded icon resource '{IconResourceName}' is missing.");
        using StreamReader reader = new(stream);
        string svg = reader.ReadToEnd();

        const int pixels = 128;
        return DrawingUtilities.BitmapFromSvg(svg, pixels, pixels, darkMode);
    }
}
