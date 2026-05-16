namespace RhMcp.Router;

// Single source of truth for on-disk paths the router shares with the plugin.
// All router state lives under <temp>/rhino-mcp/: state.db (slot registry) and
// listeners/*.json (plugin->router doorbell announcements). The plugin writes
// into the listeners dir; the router consumes from it.
public static class RouterPaths
{
    public const string BaseDirName = "rhino-mcp";
    public const string ListenersDirName = "listeners";
    public const string StateDbName = "state.db";

    public static string BaseDir => Path.Combine(Path.GetTempPath(), BaseDirName);
    public static string ListenersDir => Path.Combine(BaseDir, ListenersDirName);
    public static string StateDbPath => Path.Combine(BaseDir, StateDbName);

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(ListenersDir);
    }
}
