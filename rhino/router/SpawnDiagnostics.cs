namespace RhMcp.Router;

// Classifies the exception shapes the spawn pipeline can throw before the router
// ever talks to a child Rhino. Both spawn_slot (SpawnSlotTool) and the lazy
// default-Rhino auto-spawn (ProxyDispatcher) hit the same pipeline, so the
// exception->code/base-message mapping has one owner here. Each caller appends
// its own next-action suffix (spawn_slot vs tool-call retry advice).
//
// Caller-specific shapes (SlotNotFound, non-connection HTTP, InvalidOperationException
// codes) are NOT covered: TryClassify returns false and the caller handles them.
public static class SpawnDiagnostics
{
    // `BaseMessage` is the diagnosis without the next-action suffix the caller adds.
    public readonly record struct SpawnDiagnosis(string Code, string BaseMessage, string? CrashReportPath = null);

    public static bool TryClassify(Exception ex, RhinoCrashReportFinder crashFinder, out SpawnDiagnosis diagnosis)
    {
        switch (ex)
        {
            case FileNotFoundException fnf:
                diagnosis = new("rhino_not_installed", fnf.Message);
                return true;

            case TimeoutException te:
                diagnosis = new(
                    "startup_timeout",
                    te.Message + " The Rhino window may be showing a license, EULA, or update dialog. Check it. " +
                    "If the rh-mcp plugin isn't loaded, install it and retry.");
                return true;

            case PlatformNotSupportedException pne:
                diagnosis = new("unsupported_platform", pne.Message);
                return true;

            // A connection-level HttpRequestException out of the spawn chain means
            // an existing Rhino we tried to reuse stopped responding (the Mac
            // _router_spawn_listener fan-out). It likely crashed between probe and call.
            case HttpRequestException hre when IsConnectionFailure(hre):
                diagnosis = new(
                    "existing_rhino_unreachable",
                    "Tried to reach a previously-spawned Rhino but its control endpoint didn't respond " +
                        $"({hre.Message}). The Rhino likely crashed between the liveness probe and this call. " +
                        "The stale slot has been pruned.",
                    crashFinder.TryFindMostRecent()?.Path);
                return true;

            default:
                diagnosis = default;
                return false;
        }
    }

    // Transport-level failure: connection errors mean the listener wasn't there
    // to answer, not that Rhino returned an error. .NET 8 exposes HttpRequestError;
    // older causes (SocketException/IOException) are also unwrapped for belt-and-braces.
    public static bool IsConnectionFailure(HttpRequestException ex)
    {
        if (ex.HttpRequestError == HttpRequestError.ConnectionError)
            return true;
        if (ex.HttpRequestError == HttpRequestError.SecureConnectionError)
            return true;
        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is System.Net.Sockets.SocketException)
                return true;
            if (inner is IOException)
                return true;
        }
        return false;
    }
}
