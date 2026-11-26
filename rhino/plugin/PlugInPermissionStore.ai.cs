#if R9
using System.Collections.Generic;
using System.Text.Json;

using Rhino.AI.PlugIns;

namespace Rhino.AI;

internal sealed record PlugInPermissionEntry(Guid Id, string Name, bool Enabled);

internal static class PlugInPermissionStore
{
    private static JsonSerializerOptions Options { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyList<PlugInPermissionEntry> Load()
    {
        if (LegacyAISettings.PluginPermissions is not string json)
            return [];

        try
        {
            PlugInPermissionEntry[] entries = JsonSerializer.Deserialize<PlugInPermissionEntry[]>(json, Options) ?? [];
            return entries.Where(e => e.Id != Guid.Empty && e.Name is not null).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void Save(IReadOnlyList<PlugInPermissionEntry> entries)
    {
        // AISettings.PluginPermissions = JsonSerializer.Serialize(entries, Options);
        PlugInRegistry.ApplySavedPermissions(ToMap(entries));
    }

    public static void ApplyToRegistry() => PlugInRegistry.ApplySavedPermissions(ToMap(Load()));

    private static Dictionary<Guid, bool> ToMap(IReadOnlyList<PlugInPermissionEntry> entries)
    {
        Dictionary<Guid, bool> map = new(entries.Count);
        foreach (PlugInPermissionEntry entry in entries)
            map[entry.Id] = entry.Enabled;
        return map;
    }
}
#endif
