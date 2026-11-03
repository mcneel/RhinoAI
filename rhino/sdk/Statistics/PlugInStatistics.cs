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

        public ModelKey(Models.IModel model) : this(model.Name, model.Vendor) { }

        public ModelKey(string name, string vendor)
        {
            Name = name.ToLowerInvariant();
            Vendor = vendor.ToLowerInvariant();
        }
    }

    private readonly record struct PlugIn(string Name, Guid Id)
    {
        public PlugIn(PlugIns.PlugInToken token) : this(token.Name, token.Id) { }
    }

    // PlugIn Image (via method?)
    // PlugIn Name / Id
    // Token Count
    // List<Statistic>

    // TODO : Move to Instanes?
    private static Dictionary<PlugIn, PlugInStatistics> Stats { get; } = [];

    public List<PlugInModelStatistic> Statistics
        => PrivateModelUsage.Select(kvp => new PlugInModelStatistic(kvp.Key.Name, kvp.Key.Vendor, kvp.Value)).ToList();

    private Dictionary<ModelKey, int> PrivateModelUsage { get; } = [];

    public int TotalTokenUsage => PrivateModelUsage.Sum(pm => pm.Value);

    public static void Collect(Agent agent, PlugIns.PlugInToken token, IEnumerable<ITurn> turn)
    {
        // TODO : Handle this check better
        if (agent.Model is null) return;

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

    internal static bool LoadFromJson(string json)
    {
        PlugInStatisticsDocument? document;
        try
        {
            document = System.Text.Json.JsonSerializer.Deserialize<PlugInStatisticsDocument>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        if (document is null) return false;

        Stats.Clear();
        foreach (PlugInUsageEntry entry in document.PlugIns)
        {
            PlugInStatistics stats = new();
            foreach (ModelUsageEntry model in entry.Models)
                stats.PrivateModelUsage[new ModelKey(model.Name, model.Vendor)] = model.TokenCount;

            Stats[new PlugIn(entry.Name, entry.Id)] = stats;
        }
        return true;
    }

    internal static string GetJson()
    {
        PlugInStatisticsDocument document = new(
            Stats.Select(kvp => new PlugInUsageEntry(
                kvp.Key.Id,
                kvp.Key.Name,
                kvp.Value.PrivateModelUsage
                    .Select(m => new ModelUsageEntry(m.Key.Name, m.Key.Vendor, m.Value))
                    .ToList()))
            .ToList());

        return System.Text.Json.JsonSerializer.Serialize(document);
    }

}

internal record PlugInModelStatistic(string ModelName, string Vendor, int TokenCount = 0) { }
