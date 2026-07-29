using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server.Extensibility;

// Tools contributed at run time by other Rhino plug-ins, and the result
// transformers they register.
//
// One instance for the whole Rhino process, shared by every McpDispatcher (the "/"
// and "/agent" endpoints, and every document's server). Plug-ins are process-global,
// so a per-dispatcher registry would just be the same set several times over.
//
// Registration is push: a contributing plug-in calls McpExtensionHost, which lands
// here. Nothing scans for providers, so there is no discovery order to reason about
// and no risk of force-loading someone else's plug-in to interrogate it. A tool
// registered before this server starts is picked up when the dispatcher first reads
// the registry; one registered after is picked up on the next tools/list, because
// the dispatcher reads live rather than caching.
//
// The compiled ToolRegistry is deliberately left immutable and separate. Its
// ByName.TryAdd throws on a duplicate, which is right for compiled tools -- a clash
// there is a build defect -- and wrong for third-party ones, where a clash must
// never take the server down. Contributed tools are rejected with a message instead.
internal sealed class McpExtensionRegistry
{
    private static readonly McpExtensionRegistry Instance = new();

    public static McpExtensionRegistry Current => Instance;

    private readonly ConcurrentDictionary<string, ProviderToolHandler> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ResultTransformer> _transforms =
        new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<ProviderToolHandler> Tools => _tools.Values;

    public bool TryGet(string name, out ProviderToolHandler handler) =>
        _tools.TryGetValue(name, out handler!);

    public string[] Names() => _tools.Keys.ToArray();

    public string Register(ProviderToolDescriptor descriptor, Func<string, CancellationToken, Task<string>> handler)
    {
        if (ExtensionProtocol.IsReservedName(descriptor.Name))
            return $"'{descriptor.Name}' uses the '{ExtensionProtocol.ReservedToolPrefix}_' prefix, which is reserved by the host";

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

    public bool Unregister(string name) =>
        !string.IsNullOrEmpty(name) && _tools.TryRemove(name, out _);

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

    public string RegisterTransform(
        string owner, int order, Func<string, string, CancellationToken, Task<string>> transform)
    {
        _transforms[owner] = new ResultTransformer(owner, order, transform);
        return "";
    }

    public bool UnregisterTransform(string owner) =>
        !string.IsNullOrEmpty(owner) && _transforms.TryRemove(owner, out _);

    // Ascending order, ties broken by owner so the chain is deterministic across
    // sessions regardless of the order plug-ins happened to register in.
    public IReadOnlyList<ResultTransformer> Transformers()
    {
        if (_transforms.IsEmpty)
            return Array.Empty<ResultTransformer>();

        List<ResultTransformer> ordered = _transforms.Values.ToList();
        ordered.Sort(static (a, b) =>
        {
            int byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Owner, b.Owner);
        });
        return ordered;
    }
}

internal sealed class ResultTransformer
{
    public string Owner { get; }
    public int Order { get; }
    public Func<string, string, CancellationToken, Task<string>> Transform { get; }

    public ResultTransformer(string owner, int order, Func<string, string, CancellationToken, Task<string>> transform)
    {
        Owner = owner;
        Order = order;
        Transform = transform;
    }
}
