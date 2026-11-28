using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

// A Scrollable sizes its content to the unwrapped width of long text, and its ClientSize lags SizeChanged.
internal sealed class FittedScrollable : Scrollable
{
    private int fittedWidth;

    public FittedScrollable(Control content)
    {
        Border = BorderType.None;
        ExpandContentWidth = true;
        Content = content;
        SizeChanged += (_, _) => FitNowAndLater();
        content.SizeChanged += (_, _) => FitNowAndLater();
    }

    public void Reveal(Control target, int margin) => Application.Instance.AsyncInvoke(() =>
    {
        if (!Loaded)
            return;

        PointF top = Content.PointFromScreen(target.PointToScreen(PointF.Empty));
        int end = Math.Max(0, ScrollSize.Height - VisibleRect.Height);
        ScrollPosition = new Point(0, Math.Min(end, Math.Max(0, (int)top.Y - margin)));
    });

    private void FitNowAndLater()
    {
        Fit();
        Application.Instance.AsyncInvoke(Fit);
    }

    private void Fit()
    {
        if (!Loaded)
            return;

        int width = ClientSize.Width;
        if (width <= 0 || width == fittedWidth)
            return;

        fittedWidth = width;
        Content.Width = width;
    }
}
