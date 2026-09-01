using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RhMcp.Router;

/// <summary>
/// Exposes tools registered with the Rhino plug-in at run time as ordinary MCP tools on this router.
/// </summary>
/// <remarks>
/// <para>
/// The router's own catalogue is generated at build time from the plug-in's source, so tools
/// registered only at run time are invisible to it. Two collaborators close that gap:
/// <see cref="ContributedToolCatalog"/> queries each slot's live registry, and
/// <see cref="ListenerAnnouncementWatcher"/> re-queries on every listener announcement — a client
/// sends <c>tools/list</c> only once, so without this a tool registered after connect would never
/// appear. Late registrations surface within one heartbeat interval (~15s).
/// </para>
/// <para>
/// The SDK appends <c>ListToolsHandler</c> results to the statically registered
/// <c>ToolCollection</c> and only routes a <c>tools/call</c> here when the name is not already in
/// that collection, so compiled tools win name clashes. The catalogue mirrors that precedence when
/// listing.
/// </para>
/// </remarks>
internal sealed class ContributedTools : IDisposable
{
    private readonly ContributedToolCatalog _catalog;
    private readonly ProxyDispatcher _dispatcher;
    private readonly ILogger<ContributedTools> _log;
    private readonly ListenerAnnouncementWatcher _watcher;

    /// <summary>
    /// Captured from the first request rather than resolved from DI, which also means it is null
    /// exactly while no client has connected — the window in which notifying would be illegal.
    /// </summary>
    private volatile McpServer? _server;

    public ContributedTools(ContributedToolCatalog catalog, ProxyDispatcher dispatcher,
        ILogger<ContributedTools> log)
    {
        _catalog = catalog;
        _dispatcher = dispatcher;
        _log = log;
        _watcher = new ListenerAnnouncementWatcher(OnListenerAnnounced, log);
    }

    /// <summary>
    /// Appended by the SDK to the statically-registered tools. Must never throw: this runs on
    /// the client's connect path, and a failure here would take the whole catalogue down with it.
    /// </summary>
    public async ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        try
        {
            _server ??= request.Server;
           
            _catalog.CompiledNames = this.CompiledToolNames(request);

            await _catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);

            var tools =_catalog.Current.Values.Select(c => c.Tool);

            return new ListToolsResult { Tools = [..tools ] };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Listing contributed tools failed; serving the compiled catalogue only");
            return new ListToolsResult();
        }
    }

    /// <summary>
    /// The SDK's fallback for any name absent from the static collection, so this also receives
    /// names nobody has ever registered.
    /// </summary>
    public async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        string? name = request.Params?.Name;
       
        if (string.IsNullOrEmpty(name))
            throw new McpException("tools/call is missing a tool name.");

        if (!_catalog.TryGet(name, out ContributedTool target))
        {
            // Never listed, or registered since the last list. One refresh, then give up -- an
            // agent calling a tool it was never offered is a real error worth reporting.
            await _catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);

            if (!_catalog.TryGet(name, out target))
            {
                throw new McpException(
                    $"Unknown tool: '{name}'. If a Rhino plug-in contributes it, check that Rhino is "
                    + "running and that the plug-in registered the tool.");
            }
        }

        // Rebuilt as a JsonObject rather than a typed payload: the schema is a contributor's JSON,
        // not a .NET signature, and the NativeAOT publish forbids the reflection route.
        JsonObject arguments = [];
        if (request.Params?.Arguments is { } supplied)
        {
            foreach (KeyValuePair<string, JsonElement> pair in supplied)
                arguments[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
        }

        // slotId is never null, so ProxyDispatcher takes its explicit-slot branch and cannot
        // auto-spawn a Rhino -- which would be one without the contributing plug-in loaded, and
        // would fail with an opaque "unknown tool" from the plugin. A slot that died since we
        // resolved it yields the dispatcher's existing slot_not_found envelope.
        string json = await _dispatcher
            .CallToolAsync(target.SlotId, target.Tool.Name, arguments, cancellationToken)
            .ConfigureAwait(false);

        return new CallToolResult { Content = [new TextContentBlock { Text = json }] };
    }

    /// <summary>
    /// A Rhino announced a listener, so its tool set may have changed. Refresh, and tell the
    /// client only if it actually did — the plugin's fifteen-second heartbeat rings this for
    /// every live Rhino, forever, and an unconditional notification would be a storm. The same
    /// comparison stops refresh → adopt → notify → re-list looping.
    /// </summary>
    private void OnListenerAnnounced()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (!await _catalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false)) return;
                if (_server is not { } server) return;

                await server.SendNotificationAsync(
                    NotificationMethods.ToolListChangedNotification, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Refresh after a listener announcement failed");
            }
        });
    }

    /// <summary>
    /// The names this router's own catalogue serves — the generated proxies plus the
    /// router-local slot tools — read from the SDK's tool collection.
    /// </summary>
    private HashSet<string> CompiledToolNames(RequestContext<ListToolsRequestParams> request)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        if (request.Server.ServerOptions.ToolCollection is { } collection)
        {
            foreach (string name in collection.PrimitiveNames) names.Add(name);
        }
        return names;
    }

    /// <inheritdoc />
    public void Dispose() => _watcher.Dispose();
}
