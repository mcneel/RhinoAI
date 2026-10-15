using System;
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
        => PrivateDefinitions.TryGetValue(Instances.Preferences.AgentId, out IAgentDefinition? def) ? def : null;

    public bool Add(IAgentDefinition definition)
        => PrivateDefinitions.TryAdd(definition.Id, definition);

    public bool Exists(IAgentDefinition definition)
        => PrivateDefinitions.ContainsKey(definition.Id);

    private bool LoadAgentsFromJson(string json)
    {
        try
        {
            // TODO : Get Claude to write this
            // See : Definitions.json
            return true;
        }
        catch
        {
            return false;
        }
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

internal enum AgentType { Cli, Api, Local }
