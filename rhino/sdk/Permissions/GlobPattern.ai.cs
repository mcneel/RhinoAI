using System;

namespace Rhino.AI;

public readonly record struct GlobPattern(string Pattern, char Separator, bool IgnoreCase)
{

    public bool IsMatch(string text) => IsMatch(Pattern.AsSpan(), text.AsSpan());

    private bool IsMatch(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text)
    {
        while (true)
        {
            if (pattern.IsEmpty) return text.IsEmpty;

            if (pattern[0] == '*')
            {
                int stars = 0;
                while (stars < pattern.Length && pattern[stars] == '*') stars++;

                ReadOnlySpan<char> rest = pattern[stars..];
                bool crossesSeparators = stars > 1;

                // "a/**/b" has to match "a/b" too, or every rule would need a second copy without the middle segment.
                if (crossesSeparators && !rest.IsEmpty && rest[0] == Separator && IsMatch(rest[1..], text)) return true;

                for (int consumed = 0; consumed <= text.Length; consumed++)
                {
                    if (!crossesSeparators && consumed > 0 && text[consumed - 1] == Separator) break;
                    if (IsMatch(rest, text[consumed..])) return true;
                }

                return false;
            }

            if (pattern[0] == '?')
            {
                if (text.IsEmpty || text[0] == Separator) return false;
                pattern = pattern[1..];
                text = text[1..];
                continue;
            }

            if (text.IsEmpty || !CharsEqual(pattern[0], text[0])) return false;
            pattern = pattern[1..];
            text = text[1..];
        }
    }

    private bool CharsEqual(char a, char b) => a == b || (IgnoreCase && char.ToUpperInvariant(a) == char.ToUpperInvariant(b));

}
