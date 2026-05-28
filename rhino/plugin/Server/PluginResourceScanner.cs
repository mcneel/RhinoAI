using System.IO;
using System.Text;

using Rhino.PlugIns;

namespace RhMcp.Server;

// Two entry points:
//
//   Scan(ownPluginId)        — walks every installed 3rd-party Rhino plug-in
//                              looking for `<plugin-folder>/mcp/*.md`. Emits
//                              URIs `rhino://plugin/<slug>/<path>`.
//
//   ScanBuiltInFolder(path)  — delegates to BuiltInResourceScanner; emits URIs
//                              `rhino://<path>` for our own plug-in's primers.
//
// Best-effort: any individual plug-in that throws is skipped silently — one
// bad plug-in shouldn't take down the whole catalog.
internal static class PluginResourceScanner
{
    public const long MaxFileBytes = BuiltInResourceScanner.MaxFileBytes;

    public static List<PluginResource> Scan(Guid? ownPluginId = null)
    {
        List<PluginResource> results = new();
        HashSet<string> seenSlugs = new(StringComparer.Ordinal);

        Dictionary<Guid, string> installed;
        try { installed = PlugIn.GetInstalledPlugIns(); }
        catch { return results; }

        foreach (KeyValuePair<Guid, string> kv in installed)
        {
            if (ownPluginId.HasValue && kv.Key == ownPluginId.Value)
                continue;

            PlugInInfo? info = SafeGetInfo(kv.Key);
            if (info is null)
                continue;

            string? pluginFolder = TryGetPluginFolder(info);
            if (pluginFolder is null)
                continue;

            string mcpFolder = Path.Combine(pluginFolder, "mcp");
            if (!Directory.Exists(mcpFolder))
                continue;

            string displayName = string.IsNullOrWhiteSpace(info.Name) ? kv.Value : info.Name;
            string slug = ReserveSlug(displayName, kv.Key, seenSlugs);

            try
            {
                ScanThirdPartyFolder(slug, displayName, mcpFolder, results);
            }
            catch
            {
                // Skip this plug-in; keep going for the rest.
            }
        }

        return results;
    }

    public static List<PluginResource> ScanBuiltInFolder(string mcpFolder) =>
        BuiltInResourceScanner.ScanFolder(mcpFolder);

    private static void ScanThirdPartyFolder(
        string slug, string displayName, string mcpFolder, List<PluginResource> results)
    {
        List<PluginResource> pluginEntries = new();
        bool hasExplicitIndex = false;

        foreach (BuiltInResourceScanner.DiscoveredFile f in BuiltInResourceScanner.EnumerateMarkdown(mcpFolder))
        {
            bool isIndexFile = string.Equals(f.WithoutExt, "index", StringComparison.OrdinalIgnoreCase);

            string uri;
            string name;
            if (isIndexFile)
            {
                uri = $"rhino://plugin/{slug}";
                name = slug;
                hasExplicitIndex = true;
            }
            else
            {
                uri = $"rhino://plugin/{slug}/{f.WithoutExt}";
                name = $"{slug}/{f.WithoutExt}";
            }

            pluginEntries.Add(new PluginResource
            {
                Uri = uri,
                Name = name,
                Description = BuiltInResourceScanner.TryExtractDescription(f.AbsolutePath),
                FilePath = f.AbsolutePath,
            });
        }

        if (pluginEntries.Count == 0)
            return;

        if (!hasExplicitIndex)
        {
            pluginEntries.Add(new PluginResource
            {
                Uri = $"rhino://plugin/{slug}",
                Name = slug,
                Description = $"Catalog of resources shipped by the {displayName} plug-in.",
                IsIndex = true,
                IndexBody = RenderIndex(displayName, pluginEntries),
            });
        }

        results.AddRange(pluginEntries);
    }

    private static string RenderIndex(string displayName, IEnumerable<PluginResource> entries)
    {
        StringBuilder sb = new();
        sb.Append("# ").AppendLine(displayName);
        sb.AppendLine();
        sb.Append("Resources shipped by the ").Append(displayName).AppendLine(" plug-in.");
        sb.AppendLine();

        List<PluginResource> ordered = entries
            .OrderBy(e => e.Uri, StringComparer.Ordinal)
            .ToList();

        foreach (PluginResource e in ordered)
        {
            sb.Append("- `").Append(e.Uri).Append('`');
            if (!string.IsNullOrEmpty(e.Description))
                sb.Append(" — ").Append(e.Description);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string ReserveSlug(string? displayName, Guid id, HashSet<string> seen)
    {
        string fallback = id.ToString("N").Substring(0, 8);
        string baseSlug = Slugify(displayName) ?? fallback;

        if (seen.Add(baseSlug))
            return baseSlug;

        string suffixed = $"{baseSlug}-{fallback}";
        seen.Add(suffixed);
        return suffixed;
    }

    // lowercase, non-alphanumeric → `-`, collapse runs, trim. Returns null for
    // inputs that produce an empty slug (e.g. all-punctuation names).
    private static string? Slugify(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        StringBuilder sb = new(s.Length);
        bool lastDash = true;
        foreach (char raw in s)
        {
            char c = char.ToLowerInvariant(raw);
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }

        string slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? null : slug;
    }

    private static string? TryGetPluginFolder(PlugInInfo info)
    {
        try
        {
            string? path = info.FileName;
            if (string.IsNullOrEmpty(path))
                return null;
            return Path.GetDirectoryName(path);
        }
        catch
        {
            return null;
        }
    }

    private static PlugInInfo? SafeGetInfo(Guid id)
    {
        try { return PlugIn.GetPlugInInfo(id); }
        catch { return null; }
    }
}
