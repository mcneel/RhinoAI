namespace Rhino.AI.UI.Settings;

internal static class Phrase
{
    public static string Count(int count, string one, string many) => count == 1 ? one : string.Format(many, count);

    public static bool Contains(string text, string query) => text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
}
