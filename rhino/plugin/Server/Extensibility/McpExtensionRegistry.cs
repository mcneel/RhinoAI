using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server.Extensibility;

/// <summary>
/// Tools contributed at run time by other Rhino plug-ins. See <see cref="McpExtensionHost"/>
/// for the entry point contributors call.
/// </summary>
/// <remarks>
/// <para>
/// One instance for the whole Rhino process, shared by every <c>McpDispatcher</c> (the "/" and
/// "/agent" endpoints, and every document's server). Plug-ins are process-global, so a
/// per-dispatcher registry would just be the same set several times over.
/// </para>
/// <para>
/// Registration is push: a contributing plug-in calls <see cref="McpExtensionHost"/>, which
/// lands here. Nothing scans for providers, so there is no discovery order to reason about and
/// no risk of force-loading someone else's plug-in to interrogate it. A tool registered before
/// this server starts is picked up when the dispatcher first reads the registry; one registered
/// after is picked up on the next <c>tools/list</c>, because the dispatcher reads live rather
/// than caching.
/// </para>
/// <para>
/// The compiled <c>ToolRegistry</c> is deliberately left immutable and separate. Its
/// <c>ByName.TryAdd</c> throws on a duplicate, which is right for compiled tools — a clash there
/// is a build defect — and wrong for third-party ones, where a clash must never take the server
/// down. Contributed tools are rejected with a message instead.
/// </para>
/// <para>
/// Every member is safe to call from any thread: the backing stores are
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>, and contributors register from their own
/// plug-in load path while the server reads from request threads.
/// </para>
/// </remarks>
internal sealed class McpExtensionRegistry
{
    private static readonly McpExtensionRegistry Instance = new();

    /// <summary>
    /// The process-wide registry. There is deliberately no public constructor.
    /// </summary>
    public static McpExtensionRegistry Current => Instance;

    private readonly ConcurrentDictionary<string, ProviderToolHandler> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every currently registered contributed tool, in no particular order.
    /// </summary>
    /// <remarks>
    /// A live view over the backing store, not a snapshot: enumerating it while another plug-in
    /// registers or unregisters is safe and will not throw, but may or may not observe that
    /// change. Callers that need a stable set for the duration of a request should materialise
    /// it themselves.
    /// </remarks>
    public IEnumerable<ProviderToolHandler> Tools => _tools.Values;

    /// <summary>
    /// Looks up a contributed tool by name, case-insensitively.
    /// </summary>
    /// <param name="name">The tool name as an MCP client would send it.</param>
    /// <param name="handler">The registered handler, or null when not found.</param>
    /// <returns>True when a tool of that name is registered.</returns>
    public bool TryGet(string name, out ProviderToolHandler handler) =>
        _tools.TryGetValue(name, out handler!);

    /// <summary>
    /// The names of all registered contributed tools, as a snapshot.
    /// </summary>
    /// <returns>A new array; safe to hold and iterate.</returns>
    public string[] Names() => _tools.Keys.ToArray();

    /// <summary>
    /// Registers, or re-registers, a tool contributed by another plug-in.
    /// </summary>
    /// <param name="descriptor">Name, owner and schema of the tool being contributed.</param>
    /// <param name="handler">
    /// Invoked with the raw JSON arguments and a cancellation token; returns the raw JSON result.
    /// </param>
    /// <returns>
    /// An empty string on success, or a human-readable reason the registration was refused.
    /// Failures are returned rather than thrown: a clash between third-party plug-ins must never
    /// take the server down, and the caller is a plug-in load path that cannot usefully handle an
    /// exception.
    /// </returns>
    /// <remarks>
    /// Re-registering the same name by the same owner is an update, not a clash, so a plug-in
    /// that reloads or refreshes its schema does not have to unregister first. A different owner
    /// claiming a name already in use is refused, as is any name inside the host's reserved
    /// <see cref="ExtensionConstants.ReservedToolPrefix"/> namespace.
    /// </remarks>
    public string Register(ProviderToolDescriptor descriptor, Func<string, CancellationToken, Task<string>> handler)
    {
        if (this.IsReservedName(descriptor.Name))
            return $"'{descriptor.Name}' uses the '{ExtensionConstants.ReservedToolPrefix}_' prefix, which is reserved by the host";

        ProviderToolHandler entry = new(descriptor, handler);

        // Re-registering the same name by the same owner is an update, not a clash --
        // a plug-in that reloads or refreshes its schema should not have to
        // unregister first.
        if (_tools.TryGetValue(descriptor.Name, out ProviderToolHandler? existing)
            && !string.Equals(existing.Owner, descriptor.Owner, StringComparison.OrdinalIgnoreCase))
        {
            return $"'{descriptor.Name}' is already registered by '{existing.Owner}'";
        }

        _tools[descriptor.Name] = entry;
        return "";
    }

    /// <summary>
    /// Removes a single contributed tool by name.
    /// </summary>
    /// <param name="name">The tool name. Null and empty are accepted and do nothing.</param>
    /// <returns>True when a tool was removed; false when no such tool was registered.</returns>
    /// <remarks>
    /// Deliberately does not check ownership — a plug-in shutting down should be able to clean up
    /// without proving anything. Use <see cref="UnregisterByOwner"/> for the usual shutdown path.
    /// </remarks>
    public bool Unregister(string name) =>
        !string.IsNullOrEmpty(name) && _tools.TryRemove(name, out _);

    /// <summary>
    /// Removes every tool contributed by one owner. The normal call on plug-in shutdown.
    /// </summary>
    /// <param name="owner">The owner string the tools were registered with.</param>
    /// <returns>How many tools were removed; zero when the owner is null, empty or unknown.</returns>
    /// <remarks>
    /// Removal is not atomic across the set: a concurrent <c>tools/list</c> may observe the owner
    /// partly removed. That is acceptable because the alternative — locking the registry across a
    /// whole shutdown — would let a misbehaving plug-in stall every request thread.
    /// </remarks>
    public int UnregisterByOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return 0;

        int removed = 0;
        foreach (KeyValuePair<string, ProviderToolHandler> pair in _tools)
        {
            if (string.Equals(pair.Value.Owner, owner, StringComparison.OrdinalIgnoreCase)
                && _tools.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }
        return removed;
    }

    /// <summary>
    /// Whether <paramref name="name"/> falls inside the host's reserved
    /// <see cref="ReservedToolPrefix"/> namespace.
    /// </summary>
    /// <param name="name">The candidate name, already known to be well formed.</param>
    /// <returns>True when a contributing plug-in must not be allowed to register it.</returns>
    private bool IsReservedName(string name) =>
        name.StartsWith(ExtensionConstants.ReservedToolPrefix + "_", StringComparison.OrdinalIgnoreCase);
}
