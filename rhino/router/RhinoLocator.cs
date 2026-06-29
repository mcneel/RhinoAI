using System.Runtime.InteropServices;

namespace RhMcp.Router;

// Resolves a full path to Rhino.exe (Windows) or the Rhinoceros binary (macOS)
// for a given version string. Versions: "8" | "9" | "BETA" | "WIP".
public class RhinoLocator
{
    // Rhino 9 ships concurrently as a release candidate (BETA) and a WIP, and a
    // user-opened build of any of the three announces itself as "9". So "9",
    // "BETA", and "WIP" are interchangeable for resolution and all probe the
    // same dirs in the same order: prefer the release, then BETA, then WIP.
    public string ResolveRhinoExe(string version)
    {
        if (TryResolve(version, out string path))
            return path;

        throw new FileNotFoundException(
            $"Could not locate Rhino executable for version '{version}'. " +
            $"Installed versions found: {string.Join(", ", ListInstalledVersions())}");
    }

    private bool TryResolve(string version, out string path)
    {
        path = string.Empty;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // TODO: also try registry-based lookup if Program Files paths miss.
            string[] rhino9 =
            {
                @"C:\Program Files\Rhino 9",
                @"C:\Program Files\Rhino 9 BETA",
                @"C:\Program Files\Rhino 9 WIP",
            };
            string[] dirs = version switch
            {
                "8" => new[] { @"C:\Program Files\Rhino 8" },
                "9" or "BETA" or "WIP" => rhino9,
                _ => Array.Empty<string>()
            };
            foreach (string dir in dirs)
            {
                string candidate = Path.Combine(dir, "System", "Rhino.exe");
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
            // See Windows branch — "9", "BETA", "WIP" all probe the same apps.
            string[] rhino9 = { "Rhino 9.app", "RhinoBETA.app", "RhinoWIP.app" };
            string[] appNames = version switch
            {
                "8" => new[] { "Rhino 8.app" },
                "9" or "BETA" or "WIP" => rhino9,
                _ => Array.Empty<string>()
            };
            // Without this guard an unknown version resolves to "/Applications/",
            // which Directory.Exists trivially confirms — spawn then attempts
            // `open -a /Applications/` and we burn the full startup timeout
            // before reporting failure. Fail fast with rhino_not_installed instead.
            foreach (string appName in appNames)
            {
                string candidate = $"/Applications/{appName}";
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

    public IEnumerable<string> ListInstalledVersions()
    {
        // "9" stands in for the whole 9-family (release / BETA / WIP), which all
        // resolve identically — listing them separately would just duplicate.
        foreach (string v in new[] { "8", "9" })
        {
            if (TryResolve(v, out _))
                yield return v;
        }
    }
}
