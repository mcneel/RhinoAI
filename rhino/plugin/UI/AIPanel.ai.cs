using Eto.Forms;

namespace Rhino.AI.UI;

public partial class AIPanel
{
    public static Guid PanelId => typeof(AIPanel).GUID;

    // A newly loaded page holds no tokens, so the fingerprint has to be cleared or the resend is suppressed.
    private void OnPageLoaded(object? sender, WebViewLoadedEventArgs e)
    {
        ThemeFingerprint = null;
        OnThemeChanged(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && DataContext is AIPanelViewModel model)
            model.Dispose();

        base.Dispose(disposing);
    }
}
