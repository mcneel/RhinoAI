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

    internal static PlugInRegistry Registry { get; private set; } = new();

    internal static PlugInStatistics Statistics { get; private set; } = new();

    internal static UserConversations Conversations { get; private set; } = new();

    private static JsonSerializerOptions? Options
        => new()
        {
            AllowTrailingCommas = true,
            IgnoreReadOnlyFields = false,
            IgnoreReadOnlyProperties = false,
        };

    internal static bool LoadFromJson(string json)
    {
        try
        {
            AIInstanceShell? shell = JsonSerializer.Deserialize<AIInstanceShell>(json, Options);
            if (shell is null) return false;

            Agents = shell.Agents;
            Preferences = shell.Prefs;
            Registry = shell.Registry;
            Statistics = shell.Statistics;
        }
        catch { return false; }

        return true;
    }

    internal static string? SerializeToJson()
        => JsonSerializer.Serialize(new AIInstanceShell(Agents, Preferences, Registry, Statistics));

    private record AIInstanceShell(Agents Agents,
                                   UserPreferences Prefs,
                                   PlugInRegistry Registry,
                                   PlugInStatistics Statistics);

}
