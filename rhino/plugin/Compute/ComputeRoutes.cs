using System.Net.Http;
using System.Text.RegularExpressions;
using Rhino.Compute;

namespace RhMcp.Compute;

// Compute names its endpoints by method signature, so R8 (Evaluate is 2-arg) and
// R9 (Evaluate is 3-arg) expose different python routes. Whichever Rhino starts
// Grasshopper first owns the singleton compute server on the box, and its
// embedded Rhino version dictates which route name is exposed. Discover the
// route from /sdk once per WebAddress so the plugin works against either.
internal static class ComputeRoutes
{
    private const string FallbackPythonEvaluate = "rhino/python/evaluate-string_archivabledictionary_stringarray";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static readonly object _lock = new();
    private static readonly Dictionary<string, string> _pythonByServer = new(StringComparer.Ordinal);

    public static string PythonEvaluatePath()
    {
        var baseUrl = ComputeServer.WebAddress.TrimEnd('/');
        lock (_lock)
        {
            if (_pythonByServer.TryGetValue(baseUrl, out var cached)) return cached;
            var route = Discover(baseUrl) ?? FallbackPythonEvaluate;
            _pythonByServer[baseUrl] = route;
            return route;
        }
    }

    private static string? Discover(string baseUrl)
    {
        try
        {
            using var resp = _http.GetAsync($"{baseUrl}/sdk").GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return null;
            var html = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var m = Regex.Match(html, @">\s*HTTP:\s*POST\s+(rhino/python/evaluate[^\s<]*)\s*<");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}
