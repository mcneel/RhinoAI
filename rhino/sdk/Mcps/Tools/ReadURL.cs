using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Rhino.AI.Tools;

/// <summary>
/// An MCP tool to read a resource(s)
/// </summary>
public sealed record ReadURL(IHarness Harness) : Tool("read_url",
                                "Used for reading a resource at a URL from the web",
                                false,
                                false,
                                [
                                    new ("url", "The URL to read from", ToolArgType.URL, true),
                                    new ("keywords", "Keywords to search for, separated by ';'", ToolArgType.String, false),
                                    new ("max_token_count", "The maximum number of tokens to return", ToolArgType.Integer, false, 1_000)
                                ])
{

    private const int KeywordContextChars = 300;

    private static Regex HiddenHtml { get; } = new(@"<(script|style|noscript|svg)\b[\s\S]*?</\1\s*>|<!--[\s\S]*?-->", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static Regex HtmlTag { get; } = new(@"<[^>]+>", RegexOptions.Compiled);
    private static Regex InlineWhitespace { get; } = new(@"[ \t\f\v ]+", RegexOptions.Compiled);
    private static Regex BlankLines { get; } = new(@"\s*\n\s*", RegexOptions.Compiled);
    private static Regex NonWord { get; } = new(@"[^a-z0-9]+", RegexOptions.Compiled);
    private static Regex IgnoreInstructions { get; } = new(@"\b(ignore|disregard|forget)( (all|any|of|the|your|my|these|those))* (previous|prior|above|earlier|preceding) (instruction|instructions|prompt|prompts|directions)\b", RegexOptions.Compiled);

    private static HttpClient Client { get; } = new();

    public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!args.TryGetUrl(Args[0].Name, out string url))
            return ToolReturn.MissingArgument(Name, Args[0].Name, "a valid url");

        args.TryGetString(Args[1].Name, out string keywordString);
        string[] keywords = keywordString?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        if (!args.TryGetInt(Args[2].Name, out int max))
            Args[2].TryGetAs(out max);

        HttpResponseMessage response = await Client.GetAsync(url, token);
        if (!response.IsSuccessStatusCode)
            return ToolReturn.HttpFailure(response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(token);

        bool isHtml = response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) ?? false;
        body = CompactBody(isHtml ? ToPlainText(body) : body, max, keywords);

        // Detect after compact as the injection may be outside the filtered text
        if (DetectPromptInjection(body))
        {
            return ToolReturn.Failure("The returned content likely included prompt injection", "The resource you are looking for should not be accessed by an AI.");
        }

        return ToolReturn.Success(new TextContent(body));
    }

    private static string ToPlainText(string html)
    {
        string text = HiddenHtml.Replace(html, " ");
        text = HtmlTag.Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = InlineWhitespace.Replace(text, " ");
        return BlankLines.Replace(text, "\n").Trim();
    }

    private static string CompactBody(string body, int maxTokens, string[] keywords)
    {
        int budget = Math.Max(0, maxTokens) * TokenEstimate.CharsPerToken;
        if (body.Length <= budget) return body;

        List<(int Start, int End)> windows = KeywordWindows(body, keywords);
        if (windows.Count == 0) return body[..budget];

        StringBuilder compact = new(budget);
        foreach ((int start, int end) in windows)
        {
            int remaining = budget - compact.Length;
            if (remaining <= 0) break;

            if (compact.Length > 0) compact.Append("\n...\n");
            compact.Append(body, start, Math.Min(end - start, remaining));
        }

        return compact.ToString();
    }

    private static List<(int Start, int End)> KeywordWindows(string body, string[] keywords)
    {
        List<(int Start, int End)> hits = [];
        foreach (string keyword in keywords)
        {
            int index = body.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                hits.Add((Math.Max(0, index - KeywordContextChars), Math.Min(body.Length, index + keyword.Length + KeywordContextChars)));
                index = body.IndexOf(keyword, index + keyword.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        hits.Sort((a, b) => a.Start.CompareTo(b.Start));

        List<(int Start, int End)> merged = new(hits.Count);
        foreach ((int Start, int End) hit in hits)
        {
            if (merged.Count > 0 && hit.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, hit.End));
            else
                merged.Add(hit);
        }

        return merged;
    }

    private static bool DetectPromptInjection(string body)
    {
        string normalised = NonWord.Replace(body.ToLowerInvariant(), " ");
        if (IgnoreInstructions.IsMatch(normalised)) return true;

        // AI : Are there other phrases or ways of detecting this?
        //      Does it make sense to check with a model that has no tools if this is dodgy?
        //      How do other Harnesses prevent this?
        return false;
    }

}
