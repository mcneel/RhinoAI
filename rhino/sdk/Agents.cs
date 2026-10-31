using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Rhino.AI;

/// <summary>
/// Storage for the current Agents
/// </summary>
public sealed class Agents
{

    private ConcurrentDictionary<Guid, IAgentDefinition> PrivateDefinitions { get; } = new();
    public IReadOnlyCollection<IAgentDefinition> Definitions => PrivateDefinitions.Values.ToList();

    public IAgentDefinition? PreferredDefinition
        => PrivateDefinitions.TryGetValue(AIInstances.Preferences.PreferedAgentId, out IAgentDefinition? def) ? def : null;

    internal bool Add(IAgentDefinition definition)
        => PrivateDefinitions.TryAdd(definition.Id, definition);

    internal bool Remove(Guid id)
        => PrivateDefinitions.TryRemove(id, out _);

    public bool Exists(IAgentDefinition definition)
        => PrivateDefinitions.ContainsKey(definition.Id);

    internal bool LoadAgentsFromJson(string json)
    {
        IReadOnlyList<AgentDefinition>? definitions = AgentDefinitionsJson.Parse(json);
        if (definitions is null) return false;

        foreach (AgentDefinition definition in definitions)
        {
            Add(definition);
        }

        return true;
    }

    internal bool LoadAgentsFromJson()
    {
        try
        {
            using Stream? stream = typeof(Agents).Assembly.GetManifestResourceStream("Rhino.AI.Definitions.json");
            using StreamReader reader = new(stream!);
            string json = reader.ReadToEnd();
            return LoadAgentsFromJson(json);
        }
        catch { }
        return false;
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
        foreach (IAgentDefinition def in Definitions)
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
