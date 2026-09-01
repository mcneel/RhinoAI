using System.Runtime.InteropServices;

namespace RhinoAI.Integration.Tests.Harness;

internal static class RhinoRouterPaths
{
    public static string ResolveBinary()
    {
        string repoRoot = FindRepoRoot();
        string rid = CurrentRid();
        string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "rhino-mcp-router.exe" : "rhino-mcp-router";
        string rhinoTarget = Environment.GetEnvironmentVariable("RhinoTarget") is { Length: > 0 } target ? target : "R9";
        string pluginOS = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" : "osx";
        string binRoot = Path.Combine(repoRoot, "rhino", "plugin", "bin", $"{rhinoTarget}-{pluginOS}");

        // Mirrors RhinoAI.csproj: bin/$(RhinoTarget)-$(PluginOS)/$(Configuration)/router/$(rid)/.
        // The test runner doesn't know which configuration the plugin was built in, so probe both.
        string[] configurations = ["Release", "Debug"];
        List<string> probed = [];
        foreach (string configuration in configurations)
        {
            string path = Path.Combine(binRoot, configuration, "router", rid, exe);
            if (File.Exists(path))
            {
                return path;
            }
            probed.Add(path);
        }

        throw new FileNotFoundException(
            $"Router binary not found. Probed:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", probed)}{Environment.NewLine}" +
            $"Build the plugin first: `dotnet build rhino/plugin/RhinoAI.csproj -c Release -p:RhinoTarget={rhinoTarget}`.",
            probed[0]);
    }

    // RHINO_MCP_HOME redirects the router+plugin shared dir to a unique location
    // (see RouterPaths.BaseDir), isolating us from the user's live slot store.
    public static Dictionary<string, string?> IsolatedEnv(string tempDir)
    {
        return new Dictionary<string, string?> { ["RHINO_MCP_HOME"] = tempDir };
    }

    public static string CreateIsolatedTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rhmcp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }

    private static string CurrentRid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }
        throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "rhino.slnx")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate repo root from the test binary's directory. " +
            "Expected to find a parent directory containing rhino.slnx.");
    }
}
