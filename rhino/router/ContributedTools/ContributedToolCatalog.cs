using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace RhMcp.Router;

/// <summary>
/// What is contributed across every running slot, right now.
/// </summary>
/// <remarks>
/// Rebuilt by asking each slot's <c>_router_list_contributed_tools</c>, which reads the plugin's
/// live <c>McpExtensionRegistry</c> — so the answer is authoritative, never inferred, and nothing
/// can go stale.
/// </remarks>
internal sealed class ContributedToolCatalog(RhinoManager manager, SlotToolClient client,
    ILogger<ContributedToolCatalog> log)
{
    private static readonly IReadOnlyDictionary<string, ContributedTool> Empty =
        new Dictionary<string, ContributedTool>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One refresh at a time: a client listing while an announcement lands should not fan out twice.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile IReadOnlyDictionary<string, ContributedTool> _current = Empty;
   
    private volatile string _signature = "";

    /// <summary>The catalogue as of the last refresh, keyed by tool name.</summary>
    public IReadOnlyDictionary<string, ContributedTool> Current => _current;

    /// <summary>
    /// Names the router's own catalogue already has — the generated proxies plus the
    /// router-local slot tools — which a contributed tool may not take.
    /// </summary>
    /// <remarks>
    /// Only knowable from a request, so it is captured on the first one and reused afterwards. A
    /// refresh triggered by an announcement has no request to read it from, and it must filter
    /// identically to a client-driven one: the sets differ only on a genuine name collision now
    /// that the pull is authoritative, but while one exists an unfiltered announcement refresh
    /// would flip the signature — and spend a <c>tools/list_changed</c> — on every fifteen-second
    /// heartbeat, forever.
    /// </remarks>
    public IReadOnlySet<string> CompiledNames { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks up a contributed tool by name against the last refresh.</summary>
    public bool TryGet(string name, out ContributedTool tool) => _current.TryGetValue(name, out tool);

    /// <summary>
    /// Rebuilds the catalogue from every running slot.
    /// </summary>
    /// <returns>
    /// True when <see cref="Current"/> now differs from what it was, which is the only thing the
    /// caller needs in order to decide whether to notify the client.
    /// </returns>
    /// <remarks>
    /// Never throws. A slot that is unreachable contributes nothing rather than failing the
    /// refresh, and an outright failure keeps the previous set instead of blanking it.
    /// </remarks>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Adopt-only. GetOrCreateDefaultAsync would spawn a Rhino, which must never be a side
            // effect of listing tools.
            manager.ScanAnnouncements();

            // Ordered by pid so two Rhinos contributing one name resolve the same way twice running.
            List<ChildRhino> slots = manager.List().Where(c => c.Pid is not null).OrderBy(c => c.Pid).ToList();

            // Concurrent, so the wall clock is the slowest slot rather than their sum -- which is
            // why the per-slot timeout alone bounds this and no whole-refresh budget is needed.
            IReadOnlyList<Tool>[] perSlot = await Task
                .WhenAll(slots.Select(slot => client.ListAsync(slot, cancellationToken)))
                .ConfigureAwait(false);

            Dictionary<string, ContributedTool> merged = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < slots.Count; i++)
            {
                foreach (Tool tool in perSlot[i])
                {
                    if (!IsContributable(tool.Name, CompiledNames)) continue;
                    if (merged.ContainsKey(tool.Name)) continue;   // a lower pid already claimed it

                    merged[tool.Name] = new ContributedTool(tool, slots[i].SlotId);
                }
            }

            // The one line that says what this decided. Everything below it is swallowed on
            // purpose -- an unreachable slot must not break tools/list -- so without this a broken
            // pull and a genuinely empty one are indistinguishable.
            log.LogInformation("Contributed tools: {Tools} from {Slots} slot(s)", merged.Count, slots.Count);

            string signature = Signature(merged);
            bool changed = signature != _signature;

            _current = merged;
            _signature = signature;
            return changed;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Refreshing contributed tools failed; keeping the previous set");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Whether a name a slot reported may be offered to a client as a contributed tool.
    /// </summary>
    /// <param name="name">The tool name exactly as the slot reported it.</param>
    /// <param name="compiled">Names the router's own catalogue already has.</param>
    /// <remarks>
    /// The input is the plugin registry's own list now, so both rules here are defense-in-depth
    /// against a peer process the router does not control rather than the primary filter. The
    /// leading-underscore rule protects the router's private control channel — a conforming
    /// plugin cannot register such a name (its <c>ToolNamePattern</c> requires a leading letter),
    /// but the reply crosses a process boundary. The compiled-name rule exists because the plugin
    /// cannot know router-side names — the generated proxies and the router-local slot tools —
    /// and the list must not show a client two tools with one name; the SDK already gives
    /// compiled tools precedence on the call side.
    /// </remarks>
    public static bool IsContributable(string name, IReadOnlySet<string> compiled)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name[0] == '_') return false;
        return !compiled.Contains(name);
    }

    /// <summary>
    /// Identity of the set as a client would see it, so an unchanged refresh costs no
    /// notification.
    /// </summary>
    private static string Signature(Dictionary<string, ContributedTool> tools) =>
        string.Join('\n', tools.Values
            .Select(c => $"{c.Tool.Name}{c.SlotId}{c.Tool.Description}{c.Tool.InputSchema.GetRawText()}")
            .OrderBy(s => s, StringComparer.Ordinal));
}
