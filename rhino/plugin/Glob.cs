
using System.IO;

namespace Rhino.AI.Paths;

internal record struct Glob(string GlobPath, bool IncludeDirs = true)
{

    private List<string>? PrivateTruePaths { get; set; }

    /// <summary>null if doesn't exist, otherwhise a string</summary>
    public IReadOnlyList<string> TruePaths => PrivateTruePaths ??= ResolveTruePaths();

    private readonly List<string> ResolveTruePaths()
    {
        if (string.IsNullOrEmpty(GlobPath)) return [];

        string globPath = GlobPath.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        string[] parts = globPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);

        List<string> paths = OperatingSystem.IsWindows() ? [""] : [Path.DirectorySeparatorChar.ToString()];
        for (int i = 0; i < parts.Length; i++)
        {
            string nextPart = parts[i];
            if (string.IsNullOrEmpty(nextPart)) continue;

            List<string> tempPaths = [];
            foreach (string path in paths)
            {
                tempPaths.AddRange(ResolveGlobPath(path, nextPart));
            }

            // Nothing found, it's a dead end
            if (tempPaths.Count == 0) return [];

            paths = tempPaths;
        }

        if (!IncludeDirs)
        {
            paths = paths.Where(p => File.Exists(p)).ToList();
        }

        // Most recently written first
        return paths.OrderByDescending(File.GetLastWriteTimeUtc).ToList();
    }

    private static List<string> ResolveGlobPath(string fullPath, string nextPart)
    {
        List<string> paths = [];
        if (string.IsNullOrEmpty(nextPart)) return paths;

#if NET48
        if (OperatingSystem.IsWindows() && fullPath.Length == 2 && fullPath[1] == ':')
            fullPath += Path.DirectorySeparatorChar;
#endif

        if (!nextPart.Contains('*'))
        {
            string path = Path.Combine(fullPath, nextPart);
            if (File.Exists(path)) return [path];
            if (Directory.Exists(path)) return [path];
            return [];
        }

        string filter = Filter(nextPart);

        foreach (string dir in Directory.EnumerateDirectories(fullPath, filter))
        {
            paths.Add(dir);
        }

        foreach (string file in Directory.EnumerateFiles(fullPath, filter))
        {
            paths.Add(file);
        }

        return paths;
    }

    private static string Filter(string nextPart)
    {
        if (string.Equals(nextPart, "**")) return "*";
        return nextPart;
    }
}
