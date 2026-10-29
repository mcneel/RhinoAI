using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Rhino.AI;

/// <summary>
/// Storage for the current Agents
/// </summary>
internal sealed class Agents
{

    private Dictionary<Guid, IAgentDefinition> PrivateDefinitions { get; } = [];
    public IReadOnlyCollection<IAgentDefinition> Definitions => PrivateDefinitions.Values;

    public IAgentDefinition? PreferredDefinition
        => PrivateDefinitions.TryGetValue(Instances.Preferences.PreferedAgentId, out IAgentDefinition? def) ? def : null;

    public bool Add(IAgentDefinition definition)
        => PrivateDefinitions.TryAdd(definition.Id, definition);
    
    public bool Remove(Guid id)
        => PrivateDefinitions.Remove(id);

    public bool Exists(IAgentDefinition definition)
        => PrivateDefinitions.ContainsKey(definition.Id);

    public bool LoadAgentsFromJson(string json)
    {
        IReadOnlyList<AgentDefinition>? definitions = AgentDefinitionsJson.Parse(json);
        if (definitions is null) return false;

        PrivateDefinitions.Clear();
        foreach (AgentDefinition definition in definitions)
        {
            Add(definition);
        }

        return true;
    }

    public bool LoadAgentsFromJson()
    {
        using Stream? stream = typeof(Agents).Assembly.GetManifestResourceStream("Rhino.AI.Definitions.json");
        using StreamReader reader = new(stream!);
        string json = reader.ReadToEnd();
        return LoadAgentsFromJson(json);
    }

    // NOTE : These are ONLY ones used for the AI Panel
    private Dictionary<uint, List<Agent>> ActiveAgents { get; } = [];

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
        foreach(IAgentDefinition def in Definitions)
        {
            if (!string.Equals(def.Vendor, vendor, StringComparison.OrdinalIgnoreCase)) continue;
            if (type == AgentType.Cli && def is not CliAgentDefinition) continue;
            if (type == AgentType.Api && def is not HttpAgentDefinition) continue;
            if (type == AgentType.Local && def is not HttpAgentDefinition) continue;
            return def;
        }

        return null;
    }

}

public enum AgentType { Unknown = 0, Cli, Api, Local }
