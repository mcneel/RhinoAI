using System.IO;
using System.Text;

namespace RhMcp.Server;

// Walks an `mcp/` folder for `*.md` files and turns them into PluginResource
// records with bare `rhino://<relpath>` URIs (no plugin/<slug> prefix). Used
// by both the plug-in (for our own built-in primers) and the router (for
// router-y primers like the slots primer). Compiles cleanly with no Rhino
// dependency, so the router project compile-links this file directly.
//
// The plug-in's 3rd-party-walker (PluginResourceScanner.Scan(Guid?)) layers
// on top by calling Slugify + ScanThirdPartyFolder, which in turn share the
// EnumerateMarkdown + TryExtractDescription helpers exposed here.
internal static class BuiltInResourceScanner
{
    // Per-file size cap. Markdown above this is excluded from the catalog so
    // agents don't see a resource they can't actually read. 100 KB comfortably
    // fits any real primer; anything larger is almost certainly meant to be a
    // tool input, not a doc.
    public const long MaxFileBytes = 100 * 1024;

    public static List<PluginResource> ScanFolder(string mcpFolder)
    {
        List<PluginResource> results = new();
        if (string.IsNullOrEmpty(mcpFolder) || !Directory.Exists(mcpFolder))
            return results;

        try
        {
            foreach (DiscoveredFile f in EnumerateMarkdown(mcpFolder))
            {
                results.Add(new PluginResource
                {
                    Uri = $"rhino://{f.WithoutExt}",
                    Name = f.WithoutExt,
                    Description = TryExtractDescription(f.AbsolutePath),
                    FilePath = f.AbsolutePath,
                });
            }
        }
        catch
        {
            // Best-effort; built-in resources are nice-to-have.
        }
        return results;
    }

    internal readonly struct DiscoveredFile
    {
        public DiscoveredFile(string absolutePath, string withoutExt)
        {
            AbsolutePath = absolutePath;
            WithoutExt = withoutExt;
        }
        public string AbsolutePath { get; }
        public string WithoutExt { get; }
    }

    // Walks `mcp/` recursively for *.md files, skipping anything over the size
    // cap. Returns the file's absolute path plus its mcp-relative URI segment
    // (forward-slashed, `.md` stripped). Yields nothing if the folder is
    // unreadable.
    internal static IEnumerable<DiscoveredFile> EnumerateMarkdown(string mcpFolder)
    {
        IEnumerable<string> mdFiles;
        try { mdFiles = Directory.EnumerateFiles(mcpFolder, "*.md", SearchOption.AllDirectories); }
        catch { yield break; }

        foreach (string filePath in mdFiles)
        {
            FileInfo fi;
            try { fi = new FileInfo(filePath); }
            catch { continue; }

            if (fi.Length > MaxFileBytes)
                continue;

            string relPath = Path.GetRelativePath(mcpFolder, filePath).Replace('\\', '/');
            string withoutExt = relPath.Substring(0, relPath.Length - 3);

            yield return new DiscoveredFile(filePath, withoutExt);
        }
    }

    // Pulls a one-line description out of YAML frontmatter, falling back to the
    // first markdown heading and then the first non-empty line. Hand-rolled so
    // we don't pull in a YAML dependency for a single field.
    internal static string? TryExtractDescription(string filePath)
    {
        try
        {
            using StreamReader sr = new(filePath);
            string? line = sr.ReadLine();

            if (line is not null && line.Trim() == "---")
            {
                while ((line = sr.ReadLine()) is not null && line.Trim() != "---")
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0)
                        continue;

                    string key = line.Substring(0, colon).Trim();
                    if (!string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string value = line.Substring(colon + 1).Trim();
                    if (value.Length >= 2 &&
                        ((value[0] == '"' && value[value.Length - 1] == '"') ||
                         (value[0] == '\'' && value[value.Length - 1] == '\'')))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                    return value.Length == 0 ? null : value;
                }
                line = sr.ReadLine();
            }

            while (line is not null)
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                    return trimmed.Substring(2).TrimEnd();
                if (trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal))
                    return trimmed;
                line = sr.ReadLine();
            }
        }
        catch
        {
            // Best-effort — a file we can't open just gets no description.
        }
        return null;
    }
}
