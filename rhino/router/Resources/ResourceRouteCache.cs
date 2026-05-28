namespace RhMcp.Router.Resources;

// URI → slot map populated at the end of every `resources/list` fan-out and
// consulted at the start of every `resources/read` to route the request to
// the slot that announced the URI. Wholesale-replaced on each list (no TTL,
// no per-entry invalidation), so a slot that disappears mid-session is
// handled by the broadcast fallback in ResourceProxy.ReadAsync rather than
// by explicit invalidation here.
//
// Singleton in DI; concurrent reads happen during fan-out and from the read
// path, so guarded by a ReaderWriterLockSlim.
internal sealed class ResourceRouteCache : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private Dictionary<string, string> _uriToSlot = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return new Dictionary<string, string>(_uriToSlot, StringComparer.Ordinal);
        }
        finally { _lock.ExitReadLock(); }
    }

    public void Replace(IEnumerable<KeyValuePair<string, string>> entries)
    {
        Dictionary<string, string> next = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> kv in entries)
            next[kv.Key] = kv.Value;

        _lock.EnterWriteLock();
        try { _uriToSlot = next; }
        finally { _lock.ExitWriteLock(); }
    }

    public bool TryGetSlotId(string uri, out string slotId)
    {
        _lock.EnterReadLock();
        try
        {
            return _uriToSlot.TryGetValue(uri, out slotId!);
        }
        finally { _lock.ExitReadLock(); }
    }

    // Drops any entries pointing at the given slot. Not called from the live
    // code path today — the wholesale-replace-on-list approach makes this a
    // future hook (e.g. for plugging into RhinoManager.CloseAsync). Kept here
    // so the API is symmetric and the tests have something to exercise.
    public void Invalidate(string slotId)
    {
        _lock.EnterWriteLock();
        try
        {
            List<string> dead = new();
            foreach (KeyValuePair<string, string> kv in _uriToSlot)
            {
                if (string.Equals(kv.Value, slotId, StringComparison.Ordinal))
                    dead.Add(kv.Key);
            }
            foreach (string uri in dead)
                _uriToSlot.Remove(uri);
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void Dispose() => _lock.Dispose();
}
