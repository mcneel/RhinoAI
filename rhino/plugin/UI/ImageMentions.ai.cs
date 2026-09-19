using System.IO;
using System.Text.RegularExpressions;

namespace Rhino.AI.UI;

internal static class ImageMentions
{
    private const string ImageExtension = @"png|jpe?g|gif|webp|bmp|tiff?";
    private const string Rooted = @"(?:[A-Za-z]:[\\/]|/|~[\\/]|file://)";

    private const RegexOptions Relaxed = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Bounded bodies and a timeout, because a tool result can be a megabyte of base64 and this runs on the UI thread.
    private static TimeSpan Budget { get; } = TimeSpan.FromMilliseconds(100);

    private static Regex[] Forms { get; } =
    [
        new(@"!?\[[^\]\r\n]*\]\(\s*(?:<(?<path>[^>\r\n]{1,400})>|(?<path>[^\s)]{1,400}))\s*(?:""[^""\r\n]*""|'[^'\r\n]*')?\s*\)", Relaxed, Budget),
        new($@"[""'`](?<path>{Rooted}[^""'`\r\n]{{0,400}}\.(?:{ImageExtension}))[""'`]", Relaxed, Budget),
        new($@"(?<path>{Rooted}[^\s""'`<>()\[\]]{{0,400}}\.(?:{ImageExtension}))", Relaxed, Budget),
    ];

    public static IReadOnlyList<string> In(string text)
    {
        if (text.Length == 0)
            return [];

        List<string> found = [];
        foreach (Regex form in Forms)
        {
            try
            {
                foreach (Match match in form.Matches(text))
                {
                    if (Resolve(match.Groups["path"].Value) is not { } path)
                        continue;
                    if (!found.Contains(path, StringComparer.OrdinalIgnoreCase))
                        found.Add(path);
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }
        return found;
    }

    private static string? Resolve(string candidate)
    {
        string spelling = candidate.Trim().Trim('<', '>');
        if (spelling.Length == 0)
            return null;

        if (spelling.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(spelling, UriKind.Absolute, out Uri? uri) || !uri.IsFile)
                return null;
            spelling = uri.LocalPath;
        }
        else if (spelling.Length > 1 && spelling[0] == '~' && (spelling[1] == '/' || spelling[1] == '\\'))
        {
            spelling = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), spelling[2..]);
        }

        return Existing(spelling)
            ?? (spelling.Contains('%') ? Existing(Uri.UnescapeDataString(spelling)) : null)
            ?? (spelling.Contains(@"\\") ? Existing(spelling.Replace(@"\\", @"\")) : null);
    }

    private static string? Existing(string spelling)
    {
        if (!ServedImages.IsImage(spelling))
            return null;

        try
        {
            if (!Path.IsPathRooted(spelling))
                return null;
            string full = Path.GetFullPath(spelling);
            return File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
