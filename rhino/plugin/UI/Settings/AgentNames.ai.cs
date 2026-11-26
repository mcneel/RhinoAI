#if R9
using System.Globalization;
using System.Text;

namespace Rhino.AI.UI.Settings;

internal static class AgentNames
{
    public static string Unique(string wanted, IEnumerable<string> taken)
    {
        string name = wanted.Trim();
        HashSet<string> used = new(taken.Select(Fold));
        if (!used.Contains(Fold(name)))
            return name;

        for (int suffix = 2; ; suffix++)
        {
            string candidate = string.Format(CultureInfo.InvariantCulture, "{0} {1}", name, suffix);
            if (!used.Contains(Fold(candidate)))
                return candidate;
        }
    }

    public static string KeyNameFor(string name)
    {
        StringBuilder core = new(name.Length);
        bool gap = false;
        foreach (char letter in name.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) == UnicodeCategory.NonSpacingMark)
                continue;

            char upper = char.ToUpperInvariant(letter);
            if (upper is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                if (gap && core.Length > 0)
                    core.Append('_');
                core.Append(upper);
                gap = false;
            }
            else
            {
                gap = true;
            }
        }

        return core.Length == 0 ? "RHINO_AI_KEY" : "RHINO_AI_" + core + "_KEY";
    }

    public static bool IsSame(string left, string right) => Fold(left) == Fold(right);

    private static string Fold(string text) => text.Trim().ToLowerInvariant();
}
#endif
