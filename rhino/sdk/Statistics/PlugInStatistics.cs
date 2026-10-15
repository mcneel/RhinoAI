using System;
using System.Linq;
using System.Collections.Generic;

namespace Rhino.AI.Statistics;

/// <summary>
/// Internalised usage statistics for the user to evaluate agent usage and performance
/// </summary>
internal class PlugInStatistics
{

    private readonly record struct ModelKey
    {
        public string Name { get; }
        public string Vendor { get; }

        public ModelKey(Models.IModel model)
        {
            Name = model.Name.ToLowerInvariant();
            Vendor = model.Vendor.ToLowerInvariant();
        }
    }

    private readonly record struct PlugIn
    {
        public string Name { get; }
        public Guid Id { get; }

        public PlugIn(PlugIns.PlugInToken token)
        {
            Name = token.Name;
            Id = token.Id;
        }
    }

    // TODO : Move to Instanes?
    private static Dictionary<PlugIn, PlugInStatistics> Stats { get; } = [];

    private Dictionary<ModelKey, int> PrivateModelUsage { get; } = [];

    public int ModelUsage => PrivateModelUsage.Sum(pm => pm.Value);

    public static void Collect(Agent agent, PlugIns.PlugInToken token, IEnumerable<ITurn> turn)
    {
        PlugIn tokenKey = new(token);
        Stats.TryGetValue(tokenKey, out PlugInStatistics? stats);
        stats ??= new PlugInStatistics();
        stats.AddTokenUsage(agent.Model, turn.Sum(t => t.TokenCount) ?? 0);
        Stats[tokenKey] = stats;
    }

    public static PlugInStatistics? Usage(PlugIns.PlugInToken token)
    {
        PlugIn tokenKey = new(token);
        Stats.TryGetValue(tokenKey, out PlugInStatistics? statistics);
        return statistics;
    }

    private void AddTokenUsage(Models.IModel model, int tokenCount)
    {
        if (tokenCount <= 0) return;

        ModelKey key = new(model);
        PrivateModelUsage.TryGetValue(key, out int count);
        PrivateModelUsage[key] = count + tokenCount;
    }

    public static bool LoadFromJson(string json)
    {
        try
        {
            Stats.Clear();

            Dictionary<PlugIn, PlugInStatistics>? dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<PlugIn, PlugInStatistics>>(json);
            if (dict is null) return false;

            foreach(KeyValuePair<PlugIn, PlugInStatistics> kvp in dict)
            {
                Stats[kvp.Key] = kvp.Value;
            }
            
            return true;
        }
        catch {}
        return false;
    }

    public static string? GetJson()
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(Stats);
            return json;
        }
        catch {}
        return null;
    }

}
