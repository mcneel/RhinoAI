using Rhino.PlugIns;

namespace RhMcp.Resources;

[McpServerResourceType]
public static class InstalledPluginsResource
{
    // TODO : Can GH be a flag or a separate endpoint?

    [McpServerResource(
        UriTemplate = "rhino://host/plugins",
        Name = "installed_plugins",
        MimeType = "application/json")]
    [Description(
        "Third-party Rhino plug-ins installed on this host (excludes plug-ins " +
        "that ship with Rhino and McNeel-authored plug-ins). Read this to find " +
        "out which domain-specific tools the user has available — e.g. Orca3D " +
        "implies naval architecture work, VisualARQ implies architecture, " +
        "RhinoNest implies nesting/CAM. Use it to bias suggestions and command " +
        "choices toward workflows the user actually has the plug-ins for.")]
    public static string Read()
    {
        List<PluginEntry> entries = new();

        Dictionary<Guid, string> installed = PlugIn.GetInstalledPlugIns();
        foreach (KeyValuePair<Guid, string> kv in installed)
        {
            PlugInInfo? info = SafeGetInfo(kv.Key);
            if (info is null) continue;
            if (info.ShipsWithRhino) continue;
            if (IsMcNeel(info.Organization)) continue;
            // TODO : Filter GH plugins?

            entries.Add(new PluginEntry
            {
                Id = kv.Key.ToString(),
                Name = string.IsNullOrWhiteSpace(info.Name) ? kv.Value : info.Name,
                Version = NullIfEmpty(info.Version),
                Description = NullIfEmpty(info.Description),
                Organization = NullIfEmpty(info.Organization),
                Website = NullIfEmpty(info.WebSite),
                PlugInType = info.PlugInType.ToString(),
                IsLoaded = info.IsLoaded,
                CommandNames = (info.CommandNames is { Length: > 0 }) ? info.CommandNames : null,
                FileTypeExtensions = (info.FileTypeExtensions is { Length: > 0 }) ? info.FileTypeExtensions : null,
            });
        }

        entries.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        PluginsPayload payload = new()
        {
            Count = entries.Count,
            Plugins = entries,
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }

    private static PlugInInfo? SafeGetInfo(Guid id)
    {
        try { return PlugIn.GetPlugInInfo(id); }
        catch { return null; }
    }

    private static bool IsMcNeel(string? organization)
    {
        if (string.IsNullOrWhiteSpace(organization))
            return false;
        return organization.IndexOf("McNeel", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

    private sealed class PluginsPayload
    {
        public int Count { get; init; }
        public List<PluginEntry> Plugins { get; init; } = new();
    }

    private sealed class PluginEntry
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Version { get; init; }
        public string? Description { get; init; }
        public string? Organization { get; init; }
        public string? Website { get; init; }
        public string PlugInType { get; init; } = "";
        public bool IsLoaded { get; init; }
        public string[]? CommandNames { get; init; }
        public string[]? FileTypeExtensions { get; init; }
    }
}
