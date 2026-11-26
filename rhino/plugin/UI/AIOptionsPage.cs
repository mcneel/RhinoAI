using System.IO;
using System.Reflection;
using System.Drawing;

using Rhino.UI;
using Rhino.Runtime;

using Rhino.AI.UI.Settings;

namespace Rhino.AI;

internal sealed class AIOptionsPage : OptionsDialogPage
{
    private const string IconResourceName = "Rhino.AI.Panel_light.svg";
    private const string DarkIconResourceName = "Rhino.AI.Panel_dark.svg";

    // _OptionsPage and _PreferencesPage find the page by this, so it must match the title passed to base.
    public const string EnglishTitle = "AI";

    private Eto.Forms.Panel? Host { get; set; }
    private SettingsView? View { get; set; }
    private bool IsSaved { get; set; }
    private Image? LightCachedImage { get; set; }
    private Image? DarkCachedImage { get; set; }

    public AIOptionsPage() : base(Localization.LocalizeString("AI", 2))
    {
    }

    public override object PageControl => Host ??= NewHost();

    // Mac's Settings UI lists pages by icon; a page with no PageImage never shows in the navigation.
    public override Image PageImage => HostUtils.RunningInDarkMode switch
    {
        true => DarkCachedImage ??= LoadIcon(DarkIconResourceName),
        _ => LightCachedImage ??= LoadIcon(IconResourceName),
    };

    public override bool OnApply()
    {
        if (View is null)
            return true;

        IsSaved = View.TryCommit();
        return IsSaved;
    }

    // Mac keeps one page for the whole session and applies it whenever it stops being current, so a saved page reloads when shown again.
    public override bool OnActivate(bool active)
    {
        if (active)
        {
            Modified = true;
            if (IsSaved && Host is Eto.Forms.Panel host)
                Show(host);
        }
        return base.OnActivate(active);
    }

    private Eto.Forms.Panel NewHost()
    {
        Eto.Forms.Panel host = new()
        {
            Width = 800,
            Height = 600,
            Padding = new Eto.Drawing.Padding(20),
        };
        host.UseRhinoStyle();
        Show(host);
        return host;
    }

    private void Show(Eto.Forms.Panel host)
    {
        SettingsView? previous = View;
        View = new SettingsView(SettingsViewModel.Load());
        View.UseRhinoStyle();
        host.Content = View;
        IsSaved = false;
        previous?.Dispose();
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
