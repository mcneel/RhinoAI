using System.Globalization;

namespace Rhino.AI.UI.Settings;

internal static class OutputTokenLimit
{
    public static bool TryParse(string text, out int? limit)
    {
        limit = null;
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return true;

        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
            return false;

        limit = parsed;
        return true;
    }

    public static string Format(int? limit) => limit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
