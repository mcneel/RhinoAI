using System;
using Eto.Drawing;

namespace RhMcp;

// Shared wrapped-text measurement for the auto-grow prompt (AIPanel) and the chat bubbles
// (MessageBubble). Both need the same answer (how many rows does this text occupy at this width,
// and how tall is that), and the line-count algorithm is the single reason either would change, so
// it lives once here and takes the font as a parameter rather than closing over a per-control field.
internal static class TextMeasure
{
    // Pixel height of the text wrapped to `width`, plus a one-line safety margin: WrappedLineCount
    // only approximates Eto's Label/TextArea wrapping (it greedily packs words by MeasureString and
    // ignores the renderer's own padding/kerning), so a spare line keeps a real one-more-line wrap
    // from clipping the last line. Over-padding is harmless whitespace; under-shooting clips.
    public static int WrappedHeight(Font font, string text, int width)
    {
        float wrapWidth = Math.Max(20, width - 6);
        int lines = 0;
        foreach (string hardLine in text.Replace("\r", string.Empty).Split('\n'))
            lines += WrappedLineCount(font, hardLine, wrapWidth);
        return (int)Math.Ceiling((Math.Max(1, lines) + 1) * font.LineHeight);
    }

    // Rows a single hard line wraps into at `width`, greedily packing whole words and char-wrapping
    // any single token wider than the available width.
    public static int WrappedLineCount(Font font, string line, float width)
    {
        if (line.Length == 0)
            return 1;

        int lines = 1;
        string current = string.Empty;
        foreach (string word in line.Split(' '))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureString(candidate).Width <= width)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
                lines++;

            float wordWidth = font.MeasureString(word).Width;
            if (wordWidth <= width)
            {
                current = word;
            }
            else
            {
                lines += (int)Math.Ceiling(wordWidth / width) - 1;
                current = string.Empty;
            }
        }
        return lines;
    }
}
