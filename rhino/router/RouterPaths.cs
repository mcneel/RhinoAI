using System;
using System.IO;

namespace RhMcp.Router;

// Shared on-disk paths the router and plugin both resolve: state.db + the
// listeners/*.json announcement drop. This file is linked into the plugin
// (RhMcp.csproj) so both assemblies compile one definition; the plugin's
// RhinoMcpHost.ListenerDropDir delegates here rather than re-typing the literals.
public static class RouterPaths
{
    public const string BaseDirName = "rhino-mcp";
    public const string ListenersDirName = "listeners";
    public const string StateDbName = "state.db";
    public const string HomeOverrideEnvVar = "RHINO_MCP_HOME";

    public static string BaseDir
    {
        get
        {
            string? overrideRoot = Environment.GetEnvironmentVariable(HomeOverrideEnvVar);
            string root = string.IsNullOrEmpty(overrideRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "McNeel")
                : overrideRoot;
            return Path.Combine(root, BaseDirName);
        }
    }

    public static string ListenersDir => Path.Combine(BaseDir, ListenersDirName);
    public static string StateDbPath => Path.Combine(BaseDir, StateDbName);

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(ListenersDir);
    }
}
