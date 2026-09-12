using System.IO;
using System.Reflection;
using System.Drawing;
using Rhino.UI;
using Rhino.Runtime;

namespace Rhino.AI;

internal sealed class AIOptionsPage : OptionsDialogPage
{
    private const string IconResourceName = "Rhino.AI.Panel_light.svg";
    private const string DarkIconResourceName = "Rhino.AI.Panel_dark.svg";

    private AISettingsPanel Panel { get; } = new();
    private Image? LightCachedImage { get; set; }
    private Image? DarkCachedImage { get; set; }

    public AIOptionsPage() : base(LOC.STR("AI"))
    {
        Panel.Width = 800;
    }

    public override object PageControl => Panel;

    // Mac's Settings UI lists pages by icon; a page with no PageImage never shows in the navigation.
    public override Image PageImage => HostUtils.RunningInDarkMode switch
    {
        true => DarkCachedImage ??= LoadIcon(DarkIconResourceName),
        _ => LightCachedImage ??= LoadIcon(IconResourceName),
    };

    public override bool OnApply() => true; // Panel.TryCommit(out _);

    public override bool OnActivate(bool active)
    {
        if (active)
            Modified = true;
        return base.OnActivate(active);
    }

    private static Bitmap LoadIcon(string resourceName)
    {
        Assembly assembly = typeof(AIOptionsPage).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded icon resource '{resourceName}' is missing.");

        using StreamReader reader = new(stream);
        string svg = reader.ReadToEnd();

        // The light and dark artwork are separate files, so the svg needs no dark-mode adjustment.
        const int pixels = 128;
        return DrawingUtilities.BitmapFromSvg(svg, pixels, pixels);
    }
}
