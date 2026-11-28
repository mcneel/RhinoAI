using System.Collections.Generic;
using System.Text.Json;

using Rhino.AI.PlugIns;
using Rhino.AI.Statistics;

namespace Rhino.AI;

/// <summary>
/// All of the static instances
/// </summary>
public static class AIInstances
{

    public static Agents Agents { get; private set; } = new();

    public static UserPreferences Preferences { get; private set; } = new();

    internal static PlugInRegistry Registry { get; } = new();

    internal static PlugInStatistics Statistics { get; private set; } = new();

    internal static UserConversations Conversations { get; private set; } = new();

    private static JsonSerializerOptions Options { get; } = new()
    {
        AllowTrailingCommas = true,
    };

    internal static bool LoadFromJson(string json, out System.Exception? exOut)
    {
        exOut = null;

        try
        {
            AIInstanceShell? shell = JsonSerializer.Deserialize<AIInstanceShell>(json, Options);
            if (shell?.Agents is null) return false;
            if (shell?.Prefs is null) return false;

            Agents = shell.Agents;
            Preferences = shell.Prefs;
            Statistics = shell.Statistics ?? Statistics;
            PlugInRegistry.ApplySavedPermissions(shell.PlugInTrust ?? []);
        }
        catch (System.Exception ex)
        {
            exOut = ex;
        }

        return exOut is null;
    }

    internal static string SerializeToJson()
        => JsonSerializer.Serialize(new AIInstanceShell(Agents, Preferences, Statistics, PlugInRegistry.SavedEntries), Options);

    private sealed record AIInstanceShell(Agents Agents,
                                          UserPreferences Prefs,
                                          PlugInStatistics? Statistics,
                                          IReadOnlyList<PlugInPermissionEntry>? PlugInTrust);

}
