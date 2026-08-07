using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

/// <summary>
/// Raises a callback when a Rhino announces a listener.
/// </summary>
/// <remarks>
/// <para>
/// The router is a stdio child of the MCP client with no inbound socket, so Rhino cannot push
/// to it directly; the only Rhino→router channel is the shared listeners directory. The plugin
/// drops an announcement there when a server starts and on a fifteen-second heartbeat, and the
/// router's scan deletes each file it consumes, so the heartbeat keeps recreating them. Each
/// drop tells the router its view of a slot's tools may be out of date — which is how a tool
/// registered after the client connected still surfaces, within one heartbeat interval.
/// </para>
/// <para>
/// Construction starts watching; disposal stops. A failure to watch is logged and swallowed,
/// since the router still works without it, just no fresher than the last <c>tools/list</c>.
/// </para>
/// </remarks>
internal sealed class ListenerAnnouncementWatcher : IDisposable
{
    /// <summary>
    /// Coalesces the burst one announcement produces: it is written to a temp sibling and moved,
    /// and the plugin re-drops for every open document at once.
    /// </summary>
    private const int DebounceMilliseconds = 200;

    private readonly Action _onAnnounced;
    private readonly ILogger _log;
    private readonly object _gate = new();

    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    public ListenerAnnouncementWatcher(Action onAnnounced, ILogger log)
    {
        _onAnnounced = onAnnounced;
        _log = log;

        try
        {
            RouterPaths.EnsureDirectories();

            FileSystemWatcher watcher = new(RouterPaths.ListenersDir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            watcher.Created += (_, _) => Ring();
            watcher.Changed += (_, _) => Ring();
            watcher.Renamed += (_, _) => Ring();

            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Cannot watch {Dir}; contributed tools will refresh on listing only",
                RouterPaths.ListenersDir);
        }
    }

    private void Ring()
    {
        lock (_gate)
        {
            _debounce ??= new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
            _debounce.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void Fire()
    {
        try
        {
            _onAnnounced();
        }
        catch (Exception ex)
        {
            // A timer callback that throws takes the process down. Nothing here is worth that.
            _log.LogDebug(ex, "Handling a listener announcement failed");
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        lock (_gate) { _debounce?.Dispose(); }
    }
}
