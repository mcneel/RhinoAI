using System;
using Eto.Drawing;
using Eto.Forms;

namespace RhMcp;

// A chat row body. The user message paints a rounded filled bubble; the agent reply renders as plain
// flush text with no background, so only the user side carries a bubble. Eto has no rounded-corner
// Panel, so the user bubble paints its own rounded background and lets a transparent Label render on
// top of it. Copy/time/token affordances live in the hover meta line below the bubble (MessageMeta).
//
// Width/height are pinned explicitly via Apply(): a wrapping Label reports its full single-line width
// if left unconstrained, which forces the row to grow sideways. A tall message simply makes a tall
// bubble that the transcript scrolls: the body is not independently scrollable, so the mouse wheel
// always scrolls the transcript rather than a nested viewport. Apply re-runs on viewport resize.
internal sealed class MessageBubble : Drawable
{
    private const float Radius = 9f;
    private const int Pad = 10;        // horizontal inset for the user bubble; kept >= Radius so text
                                       // stays inside the rounded silhouette
    private const int PadV = 6;        // vertical inset: text sits past the corner arc
    private const int MinInner = 40;   // floor so a tiny message still has a sensible minimum width

    // The agent reply has no bubble, so it skips the fill and runs flush (zero padding) to the row
    // margin; only the user message insets and paints.
    private bool User { get; }
    private int HPad { get; }
    private int VPad { get; }

    private Color Fill { get; }
    private Font BodyFont { get; }
    private Label Body { get; }

    // Mutable so a streaming assistant delta can grow this bubble in place (see Update) instead of
    // tearing it down and rebuilding; NaturalWidth/MeasuredHeight read it on the next Apply.
    public string Text { get; private set; }

    // Last width budget Apply ran with, so an in-place text Update can re-pin against the same
    // viewport without the panel having to feed it back in.
    private int LastBudget { get; set; } = -1;

    // Default width budget for an in-place Update that lands before the bubble was ever pinned to a
    // viewport (first turn, before first layout). The next ApplyBubbleWidths re-pins to the real one.
    private const int UnpinnedBudget = 320;

    public MessageBubble(string text, bool user, Font font)
    {
        Text = text;
        BodyFont = font;
        User = user;
        Fill = Color.FromArgb(0x33, 0x66, 0xCC);   // only painted for the user bubble
        HPad = user ? Pad : 0;
        VPad = user ? PadV : 0;

        Body = new Label
        {
            Text = text,
            Wrap = WrapMode.Word,
            Font = font,
            TextColor = user ? Colors.White : SystemColors.ControlText,
            TextAlignment = user ? TextAlignment.Right : TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        Padding = new Padding(HPad, VPad);
        Content = Body;
    }

    // Grow this bubble in place for a streaming assistant delta: swap the body text and re-measure.
    // Re-pin against the last width budget so the row resizes without a teardown/rebuild; if a delta
    // arrives before the bubble was ever pinned (no layout yet), fall back to a default budget.
    public void Update(string text)
    {
        if (text == Text)
            return;
        Text = text;
        Body.Text = text;
        Apply(LastBudget >= 0 ? LastBudget : UnpinnedBudget);
    }

    // Hug short messages, wrap long ones; the bubble grows as tall as its content (no inner scroll).
    // maxContentWidth is the row's width budget (viewport minus margins + scrollbar reserve).
    public void Apply(int maxContentWidth)
    {
        LastBudget = maxContentWidth;
        int maxLabel = Math.Max(MinInner, maxContentWidth - 2 * HPad);
        int inner = Math.Clamp(NaturalWidth(), MinInner, maxLabel);
        Body.Size = new Size(inner, MeasuredHeight(inner));
        Width = inner + 2 * HPad;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Only the user message wears a bubble; the agent reply is flush text with no background.
        if (User)
        {
            e.Graphics.AntiAlias = true;
            e.Graphics.FillPath(Fill, RoundedRect(new RectangleF(PointF.Empty, ClientSize), Radius));
        }
        base.OnPaint(e);
    }

    private static IGraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float clamped = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
        float d = clamped * 2f;
        GraphicsPath path = new();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Longest hard line at the body font: the width the bubble would take unwrapped.
    private int NaturalWidth()
    {
        float max = 0;
        foreach (string line in Text.Replace("\r", string.Empty).Split('\n'))
            max = Math.Max(max, BodyFont.MeasureString(line).Width);
        return (int)Math.Ceiling(max) + 2;
    }

    // Height for the wrapped body at `width`. Uses the shared line-count measure but, unlike the
    // prompt's WrappedHeight, adds no spare line: the bubble hugs its text so a short message carries
    // no empty trailing line of padding. The slightly-narrow wrap width biases the count toward over-
    // not under-counting, so dropping the spare line still won't clip the last line.
    private int MeasuredHeight(int width)
    {
        float wrapWidth = Math.Max(20, width - 6);
        int lines = 0;
        foreach (string hardLine in Text.Replace("\r", string.Empty).Split('\n'))
            lines += TextMeasure.WrappedLineCount(BodyFont, hardLine, wrapWidth);
        return (int)Math.Ceiling(Math.Max(1, lines) * BodyFont.LineHeight) + 2;
    }
}
