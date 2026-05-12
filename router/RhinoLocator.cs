using System.Runtime.InteropServices;

namespace RhMcp.Router;

// Resolves a full path to Rhino.exe (Windows) or the Rhinoceros binary (macOS)
// for a given version string. Versions: "8" | "9" | "WIP".
public class RhinoLocator
{
    public string ResolveRhinoExe(string version)
    {
        var path = TryResolve(version);
        if (path is null)
        {
            throw new FileNotFoundException(
                $"Could not locate Rhino executable for version '{version}'. " +
                $"Installed versions found: {string.Join(", ", ListInstalledVersions())}");
        }
        return path;
    }

    private string? TryResolve(string version)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // TODO: also try registry-based lookup if Program Files paths miss.
            var dir = version switch
            {
                "8" => @"C:\Program Files\Rhino 8",
                "9" => @"C:\Program Files\Rhino 9",
                "WIP" => @"C:\Program Files\Rhino 9 WIP",
                _ => null
            };
            if (dir is null) return null;
            var exe = Path.Combine(dir, "System", "Rhino.exe");
            return File.Exists(exe) ? exe : null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var appName = version switch
            {
                "8" => "Rhino 8.app",
                "9" => "Rhino 9.app",
                "WIP" => "RhinoWIP.app",
                _ => null
            };
            if (appName is null) return null;
            var appPath = $"/Applications/{appName}";
            return Directory.Exists(appPath) ? appPath : null;
        }

        return null;
    }

    public IEnumerable<string> ListInstalledVersions()
    {
        foreach (var v in new[] { "8", "9", "WIP" })
        {
            if (TryResolve(v) is not null) yield return v;
        }
    }
}
