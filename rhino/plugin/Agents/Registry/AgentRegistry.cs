using System.IO;
using System.Net.Http;

using Rhino.Runtime;

namespace Rhino.AI;

/// <summary>
/// Registry of Agents, their models and everything related.
/// </summary>
internal class AgentRegistry
{

    private List<AgentDefinition> BuiltInDefinitions { get; } = [];
    private List<AgentDefinition> CustomDefinitions { get; } = [];
    public IReadOnlyList<AgentDefinition> AllDefinitions
        => BuiltInDefinitions.Union(CustomDefinitions).ToList();

    public static AgentRegistry Instance { get; } = new();

    public AgentRegistry()
    {
        LoadDefinitions();
    }

    private sealed record AgentDefinitions(List<AgentDefinition>  Definitions);
    private void LoadDefinitions()
    {
        using Stream? stream = typeof(AgentRegistry).Assembly.GetManifestResourceStream("Rhino.AI.Agents.Definitions.json");
        if (stream is null) return;
        using StreamReader reader = new(stream);

        string json = reader.ReadToEnd();

        try
        {
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                IgnoreReadOnlyFields = false,
                IgnoreReadOnlyProperties = false,
                IncludeFields = true,
            };

            // Build In's
            AgentDefinitions? definitions = JsonSerializer.Deserialize<AgentDefinitions>(json, options);
            if (definitions is not null)
            {
                BuiltInDefinitions.AddRange(definitions. Definitions);
            }
        }
        catch (Exception ex)
        {
            HostUtils.LogDebugEvent($"Failed to load Agent Definitions {ex.Message}.\n");
        }
    }

    private static void LoadDefinitionsFromRemote()
    {
        if (!RhinoApp.IsInternetAccessAllowed) return;
        string path = @"raw.githubusercontent.com/mcneel/RhinoAI/refs/heads/rhino-9.x/rhino/plugin/Agents/Definitions.json";
        HttpClient client = new ();
        client.GetStringAsync(path).ConfigureAwait(false);
    }

    public bool TryGet(string name, out AgentDefinition def)
    {
        foreach (AgentDefinition definition in AllDefinitions)
        {
            if (!string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            def = definition;
            return true;
        }

        def = default!;
        return false;
    }
}
