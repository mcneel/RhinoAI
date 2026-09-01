namespace RhMcp.Compute;

// Pure (I/O-free) hint builder, kept separate from ComputeDiagnostics so the
// test project can compile it without pulling in the vendored Rhino.Compute
// SDK or HttpClient.
internal static class ComputeHints
{
    public const string MacOs = "macOS";
    public const string Windows = "Windows";
    public const string Linux = "Linux";

    public static IReadOnlyList<string> Build(
        string platform,
        bool reachable,
        string url,
        bool isCustomUrl,
        bool hopsInstalled)
    {
        if (reachable) return Array.Empty<string>();

        if (platform == MacOs && !isCustomUrl)
            return new[]
            {
                "Rhino Compute does not run on macOS. Set RHINO_COMPUTE_URL to point at a remote Windows compute server, or run rhinomcp from Windows.",
            };

        if (isCustomUrl)
            return new[]
            {
                $"Could not reach RHINO_COMPUTE_URL={url}. Check the server is running and the URL is correct.",
            };

        if (hopsInstalled)
            return new[]
            {
                $"Compute server not running at {url}. Open Grasshopper to start the bundled compute server (the Hops package is installed).",
            };

        return new[]
        {
            $"Compute server not running at {url}. Install the \"Hops\" package via Rhino's Package Manager and open Grasshopper to start a local compute server, or set RHINO_COMPUTE_URL to a remote server.",
        };
    }
}
