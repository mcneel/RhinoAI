using System;
using Eto.Drawing;
using Eto.Forms;

namespace RhMcp;

// The hover-revealed line under a chat message: a copy button, a vague "time since", and the turn's
// token count (only when the turn reported usage). The control itself stays laid out at a fixed height
// (Height + MinimumSize) and only its children toggle on hover, so revealing it never reflows the
// transcript: an Eto control set Visible=false collapses to zero height, so the toggle lives on the
// children, never the whole row. VerticalContentAlignment centres those children in the reserved
// height. Built once per bubble and updated in place as a streaming agent reply grows and its turn
// finally accounts for tokens. RefreshTime re-stamps the relative time on each hover so it stays
// roughly current without a timer; the figures are deliberately coarse, so a little staleness is fine.
internal sealed class MessageMeta : StackLayout
{
    private const int RowHeight = 18;   // reserved so the hover toggle never moves rows below it

    private Control Copy { get; }
    private Label TimeLabel { get; }
    private Label TokenLabel { get; }

    private string Body { get; set; } = string.Empty;
    private DateTimeOffset Stamp { get; set; }
    private bool HasUsage { get; set; }
    private bool Shown { get; set; }

    public MessageMeta(bool user, Image? copyIcon)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        VerticalContentAlignment = VerticalAlignment.Center;
        Height = RowHeight;
        MinimumSize = new Size(0, RowHeight);

        Copy = BuildCopy(copyIcon);
        TimeLabel = DimLabel();
        TokenLabel = DimLabel();

        // copy nearest the bubble's biased edge on each side; the owning column right/left-aligns this.
        if (user)
        {
            Items.Add(TokenLabel);
            Items.Add(TimeLabel);
            Items.Add(Copy);
        }
        else
        {
            Items.Add(Copy);
            Items.Add(TimeLabel);
            Items.Add(TokenLabel);
        }
        Show(false);
    }

    // A borderless icon button (matching the attach button) so the copy affordance reads as a glyph,
    // not a framed button; falls back to a plain text link when the icon fails to load.
    private Control BuildCopy(Image? copyIcon)
    {
        if (copyIcon is not null)
        {
            Rhino.UI.Controls.ImageButton button = new() { Image = copyIcon, ToolTip = "Copy message" };
            button.Click += (_, _) => { Clipboard.Instance.Text = Body; button.ToolTip = "Copied!"; };
            return button;
        }
        LinkButton link = new() { Text = "Copy", Font = SystemFonts.Default(7) };
        link.Click += (_, _) => { Clipboard.Instance.Text = Body; link.ToolTip = "Copied!"; };
        return link;
    }

    private static Label DimLabel() => new()
    {
        Font = SystemFonts.Default(7),
        TextColor = SystemColors.DisabledText,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // Reveal or hide the children; the row keeps its reserved height either way. The token segment
    // shows only when the turn actually reported usage.
    public void Show(bool shown)
    {
        Shown = shown;
        Copy.Visible = shown;
        TimeLabel.Visible = shown;
        TokenLabel.Visible = shown && HasUsage;
    }

    public void Update(string text, DateTimeOffset timestamp, TokenUsage usage)
    {
        Body = text;
        Stamp = timestamp;
        HasUsage = !usage.IsEmpty;
        RefreshTime();
        TokenLabel.Text = HasUsage ? $"{FormatTokens(usage.TotalTokens)} tok" : string.Empty;
        Show(Shown);   // re-apply visibility with the refreshed usage state
    }

    // Re-stamp the relative time; called on hover so the figure reflects now rather than last render.
    public void RefreshTime() => TimeLabel.Text = Relative(Stamp);

    private static string Relative(DateTimeOffset when)
    {
        if (when == default)
            return string.Empty;
        TimeSpan ago = DateTimeOffset.UtcNow - when;
        if (ago < TimeSpan.FromMinutes(1))
            return "just now";
        if (ago < TimeSpan.FromHours(1))
            return $"{(int)ago.TotalMinutes}m ago";
        if (ago < TimeSpan.FromDays(1))
            return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }

    private static string FormatTokens(int count) =>
        count >= 1000 ? $"{count / 1000.0:0.#}k" : count.ToString();
}
