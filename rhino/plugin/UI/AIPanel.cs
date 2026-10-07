using System.Runtime.InteropServices;

using Eto.Forms;
using Rhino.UI;
using Rhino.UI.Controls;
using Eto.Drawing;
using Rhino.Resources;

namespace Rhino.AI.UI;

// Inherited from the Eto panel this replaced, so saved Rhino layouts still resolve the AI panel.
[Guid("fb948c98-5987-45a3-8dcb-2814ed77ee3b")]
public partial class AIPanel : Panel
{
    private WebView View { get; }

    private AIPanelViewModel Model => (DataContext as AIPanelViewModel)!;

    public AIPanel() : this(RhinoDoc.ActiveDoc?.RuntimeSerialNumber ?? 0U)
    {

    }

    public AIPanel(uint documentSerialNumber)
    {
        View = new();
        DataContext = new AIPanelViewModel(View, documentSerialNumber);
        Content = new RhinoPanelTableLayout
        {
            Spacing = RhinoLayout.Spacing(RhinoLayout.SpacingType.Dialog),
            Rows =
            {
                new TableRow(new TableCell(CreateToolbar(), true)),
                new TableRow(new TableCell(View, true)){ScaleHeight = true },
            }
        };

        LoadUI();
    }

    private StackLayout CreateToolbar() // todo: update nuget and use TopRowButtonLayout
    {
        // note: the localized strings with id 63, 64 and 65 also live in PanelStrings.ai.cs and need to stay in sync!
        ImageToolTipButton history = new(ResourceIds.ToolsCommandHistorySvg, null, Localization.LocalizeString("Conversation history", 63), null);
        // The drawer belongs to the page, so the button asks for it rather than drawing it.
        history.Click += (_, _) => Model.Bridge.Post(new OverlayToggleEvent("history"));

        ImageToolTipButton newConversation = new(ResourceIds.Svg_namedItemAddHotSvg, null, Localization.LocalizeString("New conversation  (Ctrl+Shift+N)", 64), null)
        {
            MaskImageWithBackgroundColorWhenDisabled = true,
        };
        newConversation.Click += (_, _) => Model.Execute(new NewConversationCommand());
        Model.TurnRunningChanged += running => newConversation.Enabled = !running;

        ImageToolTipButton settings = new(ResourceIds.StandardStandardToolsOptionsSvg, null, Localization.LocalizeString("AI settings", 65), null);
        settings.Click += (_, _) => Model.Execute(new OpenSettingsCommand());

        ImageToolTipButton help = new(ResourceIds.Svg_namedItemHelpSvg, null, LOC.STR("Help"), null);
        help.Click += (_, _) => Model.Execute(new OpenUrlCommand(DocsLinks.Homepage));
        
        // TopRowButtonLayout is newer than the RhinoCommon package this compiles against (added 2026-06-02), 
        // so use StackLayout until the package is updated
        return new StackLayout 
        {
            Orientation = Orientation.Horizontal,
            Spacing = RhinoLayout.StackedSpacing(Orientation.Horizontal, RhinoLayout.SpacingType.Dialog),
            Items = { newConversation, history, settings, help },
        };
    }

    private void LoadUI()
    {
        View.DocumentLoaded += OnPageLoaded;
        View.Url = Model.PageUrl;

        // Make dragging the panel MUCH easier.
        // Padding = 4; // not needed with RhinoPanelTableLayout as content
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        OnThemeChanged(this, e);
        Rhino.UI.ThemeSettings.ThemeChanged += OnThemeChanged;
        Model.Attach();
    }

    private string? ThemeFingerprint { get; set; }
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        PanelTheme.Rgb Read(Eto.Drawing.Color c) => new(c.R, c.G, c.B);
        PanelTheme.Rgb Convert(System.Drawing.Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
        PanelTheme.Rgb Paint(Rhino.ApplicationSettings.PaintColor which) =>
            Convert(Rhino.ApplicationSettings.AppearanceSettings.GetPaintColor(which));

        PanelTheme.Palette palette = new(
            Chrome: Paint(Rhino.ApplicationSettings.PaintColor.PanelBackground),
            Field: Read(Rhino.UI.ThemeSettings.Content.List.Enabled.Background),
            Text: Paint(Rhino.ApplicationSettings.PaintColor.TextEnabled),
            Dim: Paint(Rhino.ApplicationSettings.PaintColor.TextDisabled),
            Border: Paint(Rhino.ApplicationSettings.PaintColor.GridLinesOnPanelBackground),
            Accent: Read(Eto.Drawing.SystemColors.Highlight),
            AccentText: Read(Eto.Drawing.SystemColors.HighlightText),
            // Rhino's own hyperlink colour. Eto's LinkText is no use here: its Windows handler maps
            // it to the selection highlight.
            Link: Convert(Rhino.ApplicationSettings.AppearanceSettings.CommandPromptHypertextColor),
            Selection: Read(Eto.Drawing.SystemColors.Selection),
            SelectionText: Read(Eto.Drawing.SystemColors.SelectionText));

        Dictionary<string, string> tokens = PanelTheme.Tokens(palette);

        // Rhino's own UI font, not Eto's SystemFonts.Default, which answers for the platform.
        Eto.Drawing.Font font = Rhino.Resources.EtoFonts.NormalFont;
        foreach (KeyValuePair<string, string> entry in PanelTheme.Fonts(font.FamilyName, font.Size, OperatingSystem.IsWindows()))
            tokens[entry.Key] = entry.Value;

        string scheme = PanelTheme.IsDarkTheme(palette) ? "dark" : "light";
        string themeFingerprint = scheme + string.Join(";", tokens.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value}"));

        if (string.Equals(ThemeFingerprint, themeFingerprint, StringComparison.OrdinalIgnoreCase)) return;
        ThemeFingerprint = themeFingerprint;

        Model.Bridge.Post(new ThemeEvent(scheme, tokens));
    }

    protected override void OnUnLoad(EventArgs e)
    {
        base.OnUnLoad(e);
        Rhino.UI.ThemeSettings.ThemeChanged -= OnThemeChanged;
        Model.Detach();
    }

}
