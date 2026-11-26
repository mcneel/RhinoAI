using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Rhino.AI.Statistics;

/// <summary>
/// Internalised usage statistics for the user to evaluate agent usage and performance
/// </summary>
internal class PlugInStatistics
{

    private readonly record struct ModelUsageKey
    {
        // DateTime.UnixEpoch does not exist on net48.
        private static DateTime UnixEpoch { get; } = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public string Name { get; }
        public string Vendor { get; }
        public int DaysSinceUnixEpoch { get; }
        public int HourInDay { get; }

        public DateTimeOffset Hour => new(UnixEpoch.AddDays(DaysSinceUnixEpoch).AddHours(HourInDay));

        public ModelUsageKey(Models.IModel model) : this(model.Name, model.Vendor) { }
        
        public ModelUsageKey(string name, string vendor) : this(name, vendor, DateTime.UtcNow) {}

        public ModelUsageKey(string name, string vendor, DateTime dateTime)
        {
            Name = name.ToLowerInvariant();
            Vendor = vendor.ToLowerInvariant();
            DaysSinceUnixEpoch = (int)(dateTime - UnixEpoch).TotalDays;
            HourInDay = dateTime.Hour;
        }
    }

    private readonly record struct PlugIn(string Name, Guid Id)
    {
        public PlugIn(PlugIns.PlugInToken token) : this(token.Name, token.Id) { }
    }

    private Dictionary<PlugIn, PlugInStatistics> Stats { get; } = [];

    [JsonInclude]
    private IReadOnlyList<PlugInHourUsage> HourlyUsage
    {
        get => Stats
            .SelectMany(plugIn => plugIn.Value.PrivateModelUsage
                .Select(usage => new PlugInHourUsage(plugIn.Key.Id, plugIn.Key.Name, usage.Key.Name, usage.Key.Vendor, usage.Key.Hour, usage.Value)))
            .ToList();
        set
        {
            Stats.Clear();
            foreach (PlugInHourUsage usage in value)
            {
                PlugIn plugIn = new(usage.Name, usage.Id);
                if (!Stats.TryGetValue(plugIn, out PlugInStatistics? stats))
                {
                    stats = new PlugInStatistics();
                    Stats[plugIn] = stats;
                }

                stats.PrivateModelUsage[new ModelUsageKey(usage.Model, usage.Vendor, usage.Hour.UtcDateTime)] = checked((int)usage.TokenCount);
            }
        }
    }

    [JsonIgnore]
    public List<PlugInModelStatistic> Statistics
        => PrivateModelUsage.Select(kvp => new PlugInModelStatistic(kvp.Key.Name, kvp.Key.Vendor, kvp.Value)).ToList();

    private Dictionary<ModelUsageKey, int> PrivateModelUsage { get; } = [];

    [JsonIgnore]
    public int TotalTokenUsage => PrivateModelUsage.Sum(pm => pm.Value);

    public static void Collect(Agent agent, PlugIns.PlugInToken token, IEnumerable<ITurn> turn)
    {
        // TODO : Handle this check better
        if (agent.Model is null) return;

        PlugIn tokenKey = new(token);
        AIInstances.Statistics.Stats.TryGetValue(tokenKey, out PlugInStatistics? stats);
        stats ??= new PlugInStatistics();
        stats.AddTokenUsage(agent.Model, turn.Sum(t => t.TokenCount) ?? 0);
        AIInstances.Statistics.Stats[tokenKey] = stats;
    }

    public static PlugInStatistics? Usage(PlugIns.PlugInToken token)
    {
        PlugIn tokenKey = new(token);
        AIInstances.Statistics.Stats.TryGetValue(tokenKey, out PlugInStatistics? statistics);
        return statistics;
    }

    internal static IReadOnlyList<PlugInHourUsage> Hourly() => AIInstances.Statistics.HourlyUsage;

    private void AddTokenUsage(Models.IModel model, int tokenCount)
    {
        if (tokenCount <= 0) return;

        ModelUsageKey key = new(model);
        PrivateModelUsage.TryGetValue(key, out int count);
        PrivateModelUsage[key] = count + tokenCount;
    }

}

internal record PlugInModelStatistic(string ModelName, string Vendor, int TokenCount = 0) { }

internal sealed record PlugInHourUsage(Guid Id, string Name, string Model, string Vendor, DateTimeOffset Hour, long TokenCount);
