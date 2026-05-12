using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RhMcp.Resources;

[McpServerResourceType]
public static class CommandHelpResource
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    [McpServerResource(
        UriTemplate = "rhino://commands/{name}/help",
        Name = "command_help",
        MimeType = "text/plain")]
    [Description("Help page text for a Rhino command, fetched from docs.mcneel.com. Pass the command name (case-insensitive, e.g. \"Box\", \"Fillet\", \"ArrayCrv\").")]
    public static async Task<string> Read(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Command name is required.", nameof(name));

        var slug = WebUtility.UrlEncode(name.Trim().ToLowerInvariant());
        var major = RhinoApp.Version.Major;
        var url = $"https://docs.mcneel.com/rhino/{major}/help/en-us/commands/{slug}.htm";

        using var response = await Http.GetAsync(url).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"No help page found for command \"{name}\" at {url}.");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var text = ExtractMainText(html);

        return $"{name} — {url}\n\n{text}";
    }

    private static readonly Regex MainContentRegex = new(
        """<div\b[^>]*\bid="mc-main-content"[^>]*>(?<body>.*?)</div>\s*</div>\s*</div>\s*</div>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex ScriptStyleRegex = new(
        @"<(script|style)\b[^>]*>.*?</\1>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex BlockBreakRegex = new(
        @"</(p|div|li|tr|h[1-6]|br)\s*>|<br\s*/?>",
        RegexOptions.IgnoreCase);

    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Singleline);

    private static readonly Regex WhitespaceRegex = new(@"[ \t]+", RegexOptions.Compiled);

    private static readonly Regex BlankLinesRegex = new(@"\n{3,}", RegexOptions.Compiled);

    private static string ExtractMainText(string html)
    {
        var match = MainContentRegex.Match(html);
        var body = match.Success ? match.Groups["body"].Value : html;

        body = ScriptStyleRegex.Replace(body, " ");
        body = BlockBreakRegex.Replace(body, "\n");
        body = TagRegex.Replace(body, " ");
        body = WebUtility.HtmlDecode(body);
        body = WhitespaceRegex.Replace(body, " ");
        body = BlankLinesRegex.Replace(body.Trim(), "\n\n");

        return body;
    }
}
