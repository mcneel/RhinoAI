using System.IO;
using System.Reflection;

using RhMcp.Server;

namespace RhMcp.Router.Resources;

// Loads router-local primers from `<router-binary-dir>/mcp/**/*.md` at
// construction. ResourceProxy serves these without consulting any slot —
// they're available even when no Rhino is running. URIs are flat
// `rhino://<relpath>`; on the (rare) cross-collision with a slot-announced
// URI, router-local wins (merge order in ResourceProxy.ListAsync).
internal sealed class RouterBuiltInResources
{
    private readonly Dictionary<string, PluginResource> _byUri;

    public RouterBuiltInResources()
    {
        List<PluginResource> scanned = BuiltInResourceScanner.ScanFolder(LocateMcpFolder());
        _byUri = new Dictionary<string, PluginResource>(StringComparer.Ordinal);
        foreach (PluginResource r in scanned)
            _byUri[r.Uri] = r;
    }

    public IReadOnlyCollection<PluginResource> All => _byUri.Values;

    public PluginResource? MatchByUri(string uri) =>
        _byUri.TryGetValue(uri, out PluginResource? r) ? r : null;

    // Sits next to the router binary. NativeAOT publishes resolve
    // Assembly.Location to "" — handle that case by falling back to
    // AppContext.BaseDirectory which AOT does populate.
    private static string LocateMcpFolder()
    {
        string? dir = Path.GetDirectoryName(typeof(RouterBuiltInResources).Assembly.Location);
        if (string.IsNullOrEmpty(dir))
            dir = AppContext.BaseDirectory;
        return string.IsNullOrEmpty(dir) ? "" : Path.Combine(dir, "mcp");
    }
}
