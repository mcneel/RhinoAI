using System.Runtime.InteropServices;

namespace RhMcp.Router;

// Resolves a full path to Rhino.exe (Windows) or the Rhinoceros app bundle (macOS)
// for a given version token. Accepted tokens are exactly the keys of VersionMap
// below: "8" | "9" | "WIP". "9" and "WIP" are aliases for the Rhino 9 family; both
// probe the release, then BETA, then WIP install dirs in that preference order.
// Rhino 9 ships concurrently as release candidate, BETA, and WIP, and a user-opened
// build of any of them announces itself as "9", so all three resolve identically.
public static class RhinoLocator
{
    // The single canonical version-token-to-install mapping, shared by both the
    // platform resolve branches and ListInstalledVersions so a token can never
    // mean one thing on disk and another in the advertised list. Each token lists
    // the install folder names to probe, in preference order: the Windows entries
    // are subfolders of C:\Program Files, the macOS entries app bundles under
    // /Applications.
    private sealed record VersionInstall(string[] WindowsFolders, string[] MacBundles);

    // The Rhino 9 family probes release -> BETA -> WIP, preferring the most stable.
    private static readonly string[] Rhino9WindowsFolders = ["Rhino 9", "Rhino 9 BETA", "Rhino 9 WIP"];
    private static readonly string[] Rhino9MacBundles = ["Rhino 9.app", "RhinoBETA.app", "RhinoWIP.app"];

    private static IReadOnlyDictionary<string, VersionInstall> VersionMap { get; } =
        new Dictionary<string, VersionInstall>(StringComparer.OrdinalIgnoreCase)
        {
            ["8"] = new VersionInstall(["Rhino 8"], ["Rhino 8.app"]),
            ["9"] = new VersionInstall(Rhino9WindowsFolders, Rhino9MacBundles),
            ["WIP"] = new VersionInstall(Rhino9WindowsFolders, Rhino9MacBundles),
        };

    public static string ResolveRhinoExe(string version)
    {
        if (TryResolve(version, out string path))
            return path;

        throw new FileNotFoundException(
            $"Could not locate Rhino executable for version '{version}'. " +
            $"Installed versions found: {string.Join(", ", ListInstalledVersions())}");
    }

    private static bool TryResolve(string version, out string path)
    {
        path = string.Empty;

        if (!VersionMap.TryGetValue(version, out VersionInstall? install))
            return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (string folder in install.WindowsFolders)
            {
                string candidate = Path.Combine(@"C:\Program Files", folder, "System", "Rhino.exe");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Each candidate is a concrete bundle name, so an unknown version can't
            // resolve to a bare "/Applications/" (the VersionMap miss above already
            // bailed) and trigger a doomed `open -a /Applications/` that burns the
            // full spawn timeout before failing.
            foreach (string bundle in install.MacBundles)
            {
                string candidate = Path.Combine("/Applications", bundle);
                if (Directory.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
            return false;
        }

        return false;
    }

    // The version tokens this locator understands, in advertised order. Kept as a
    // pure, internal seam so the token set (the source of a past mismatch between
    // the documented tokens and what was actually advertised) is unit-testable
    // without a real Rhino install on disk.
    internal static IReadOnlyList<string> KnownVersionTokens => [.. VersionMap.Keys];

    public static IEnumerable<string> ListInstalledVersions()
    {
        foreach (string version in KnownVersionTokens)
        {
            if (TryResolve(version, out _))
                yield return version;
        }
    }
}
