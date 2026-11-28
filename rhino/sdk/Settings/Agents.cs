using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// Storage for the current Agents
/// </summary>
public sealed class Agents
{

    [JsonInclude]
    [JsonPropertyName(nameof(Definitions))]
    private ConcurrentDictionary<Guid, IAgentDefinition> PrivateDefinitions { get; set; } = new();
    [JsonIgnore]
    public IReadOnlyCollection<IAgentDefinition> Definitions => PrivateDefinitions.Values.ToList();

    [JsonIgnore]
    public IAgentDefinition? PreferredDefinition
        => PrivateDefinitions.TryGetValue(AIInstances.Preferences.PreferedAgentId, out IAgentDefinition? def) ? def : null;

    internal bool Add(IAgentDefinition definition)
        => PrivateDefinitions.TryAdd(definition.Id, definition);

    internal bool Remove(Guid id)
        => PrivateDefinitions.TryRemove(id, out _);

    public bool Exists(IAgentDefinition? definition)
        => definition is not null && PrivateDefinitions.ContainsKey(definition.Id);

    private bool LoadAgentsFromJson()
    {
        try
        {
            using Stream? stream = typeof(Agents).Assembly.GetManifestResourceStream("Rhino.AI.Definitions.json");
            using StreamReader reader = new(stream!);
            string json = reader.ReadToEnd();
            
            IReadOnlyList<AgentDefinition>? definitions = AgentDefinitionsJson.Parse(json);
            if (definitions is null) return false;

            foreach (AgentDefinition definition in definitions)
            {
                Add(definition);
            }

            return true;
        }
        catch { }
        return false;
    }

    // NOTE : These are ONLY ones used for the AI Panel
    private Dictionary<uint, List<Agent>> ActiveAgents { get; } = [];

    public Agents()
    {
        LoadAgentsFromJson();
    }

    public bool TryRegisterAgent(uint id, Agent agent)
    {
        ActiveAgents.TryGetValue(id, out List<Agent>? agents);
        agents ??= new();
        agents.Add(agent);
        ActiveAgents[id] = agents;

        return true;
    }

    public IAgentDefinition? FromParameters(string vendor, AgentType type)
    {
        if (PreferredDefinition is { } preferred && Matches(preferred, vendor, type))
            return preferred;

        return Definitions.FirstOrDefault(definition => Matches(definition, vendor, type));
    }

    private static bool Matches(IAgentDefinition definition, string vendor, AgentType type)
    {
        if (!definition.Enabled) return false;
        if (!string.Equals(definition.Vendor, vendor, StringComparison.OrdinalIgnoreCase)) return false;

        return type switch
        {
            AgentType.Cli => definition is CliAgentDefinition,
            AgentType.Api or AgentType.Local => definition is HttpAgentDefinition,
            AgentType.Unknown => true,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    internal IAgentDefinition? FromId(Guid id)
    {
        PrivateDefinitions.TryGetValue(id, out IAgentDefinition? def);
        return def;
    }

}

public enum AgentType { Unknown = 0, Cli, Api, Local }
