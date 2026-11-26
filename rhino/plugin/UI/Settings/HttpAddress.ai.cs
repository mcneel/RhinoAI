namespace Rhino.AI.UI.Settings;

internal static class HttpAddress
{
    public static bool IsValid(string text) => Parse(text) is not null;

    public static Uri? Parse(string text)
    {
        string trimmed = text.Trim();
        bool hasScheme = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!hasScheme || trimmed.Any(char.IsWhiteSpace))
            return null;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) && uri.Host.Length > 0 ? uri : null;
    }
}
