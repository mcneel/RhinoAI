using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RhMcp.Compute;

internal static class ComputeDiagnostics
{
    public sealed record ComputeStatus(
        string Url,
        bool Reachable,
        string? Version,
        string Platform,
        bool IsCustomUrl,
        bool HopsInstalled,
        IReadOnlyList<string> Hints);

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public static ComputeStatus Probe()
    {
        ComputeConfig.EnsureInitialized();
        var url = ComputeConfig.CurrentUrl;
        var isCustomUrl = ComputeConfig.HasCustomUrl;
        var platform = GetPlatform();
        var hopsInstalled = IsHopsInstalled();
        var (reachable, version) = TryHealthcheck(url);
        var hints = ComputeHints.Build(platform, reachable, url, isCustomUrl, hopsInstalled);
        return new ComputeStatus(url, reachable, version, platform, isCustomUrl, hopsInstalled, hints);
    }

    public static object ToDto(ComputeStatus s) => new
    {
        url = s.Url,
        reachable = s.Reachable,
        version = s.Version,
        platform = s.Platform,
        isCustomUrl = s.IsCustomUrl,
        hopsInstalled = s.HopsInstalled,
        hints = s.Hints,
    };

    public static bool IsMacOs() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static string GetPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return ComputeHints.MacOs;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return ComputeHints.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return ComputeHints.Linux;
        return "Unknown";
    }

    // The vendored RhinoCompute SDK uses legacy HttpWebRequest, so connect-time
    // failures surface as WebException with a Status that distinguishes network
    // errors from HTTP-protocol errors. HttpRequestException is here for our
    // own HttpClient probe.
    public static bool IsConnectionFailure(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is WebException web)
            {
                switch (web.Status)
                {
                    case WebExceptionStatus.ConnectFailure:
                    case WebExceptionStatus.NameResolutionFailure:
                    case WebExceptionStatus.Timeout:
                    case WebExceptionStatus.SendFailure:
                    case WebExceptionStatus.ConnectionClosed:
                    case WebExceptionStatus.TrustFailure:
                    case WebExceptionStatus.SecureChannelFailure:
                        return true;
                }
            }
            if (e is HttpRequestException) return true;
            if (e is SocketException) return true;
        }
        return false;
    }

    public static bool IsHopsInstalled()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData)) return false;
            var packagesRoot = Path.Combine(appData, "McNeel", "Rhinoceros", "packages");
            if (!Directory.Exists(packagesRoot)) return false;
            foreach (var versionDir in Directory.EnumerateDirectories(packagesRoot))
            {
                if (Directory.Exists(Path.Combine(versionDir, "Hops"))) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static (bool reachable, string? version) TryHealthcheck(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        try
        {
            using var res = _http.GetAsync($"{trimmed}/healthcheck").GetAwaiter().GetResult();
            if (!res.IsSuccessStatusCode) return (false, null);
        }
        catch
        {
            return (false, null);
        }

        try
        {
            using var res = _http.GetAsync($"{trimmed}/version").GetAwaiter().GetResult();
            if (!res.IsSuccessStatusCode) return (true, null);
            var body = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return (true, string.IsNullOrWhiteSpace(body) ? null : body.Trim());
        }
        catch
        {
            return (true, null);
        }
    }
}
