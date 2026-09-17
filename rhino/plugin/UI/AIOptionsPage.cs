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

    // A fixed host whose content is replaced on every activation. AISettingsPanel reads AISettings in
    // its constructor, so one built at plug-in load showed whatever was true at startup for the rest
    // of the session — every change made from a panel's own settings dialog was invisible here.
    private Eto.Forms.Panel Host { get; } = new();
    private AISettingsTabs? Shown { get; set; }
    private Image? LightCachedImage { get; set; }
    private Image? DarkCachedImage { get; set; }

    public AIOptionsPage() : base(LOC.STR("AI"))
    {
    }

    public override object PageControl => Host;

    // Mac's Settings UI lists pages by icon; a page with no PageImage never shows in the navigation.
    public override Image PageImage => HostUtils.RunningInDarkMode switch
    {
        true => DarkCachedImage ??= LoadIcon(DarkIconResourceName),
        _ => LightCachedImage ??= LoadIcon(IconResourceName),
    };

    // Was returning true without committing, so OK on this page discarded everything typed into it.
    // Safe to commit now that the page is rebuilt on activation: it can only write back what it read.
    public override bool OnApply() => Shown?.TryCommit(out _) ?? true;

    public override bool OnActivate(bool active)
    {
        if (active)
        {
            Shown = new AISettingsTabs(AIProfile.Rhino) { Width = 800 };
            Host.Content = Shown;
            Modified = true;
        }
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
