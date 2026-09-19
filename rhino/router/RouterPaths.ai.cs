using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Rhino.AI.Router;

// Shared on-disk paths the router and plugin both resolve: state.db + the
// listeners/*.json announcement drop. This file is linked into the plugin
// (Rhino.AI.csproj) so both assemblies compile one definition; the plugin's
// RhinoMcpHost.ListenerDropDir delegates here rather than re-typing the literals.
public static class RouterPaths
{
    public const string VendorDirName = "McNeel";
    public const string RhinoDirName = "Rhinoceros";
    public const string BaseDirName = "ai";
    public const string ListenersDirName = "listeners";
    public const string BinDirName = "bin";
    public const string StateDbName = "state.db";
    public const string HomeOverrideEnvVar = "RHINO_MCP_HOME";

    public static string BaseDir
    {
        get
        {
            string? overrideRoot = Environment.GetEnvironmentVariable(HomeOverrideEnvVar);
            string root = string.IsNullOrEmpty(overrideRoot)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    VendorDirName,
                    RhinoDirName)
                : overrideRoot;
            return Path.Combine(root, BaseDirName);
        }
    }

    public static string ListenersDir => Path.Combine(BaseDir, ListenersDirName);
    public static string StateDbPath => Path.Combine(BaseDir, StateDbName);
    public static string BinDir => Path.Combine(BaseDir, BinDirName);

    public static string RouterExeName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "rhino-mcp-router.exe" : "rhino-mcp-router";

    // The one path every agent config spawns, stable across plugin updates.
    public static string StagedRouterExe => Path.Combine(BinDir, RouterExeName);

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(ListenersDir);
    }
}
