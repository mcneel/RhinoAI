using Rhino.AI;
using Rhino.AI.PlugIns;
using Rhino.AI.Statistics;

namespace sdk.tests;

[NonParallelizable]
public class StatisticsTests
{

    [Test]
    public void Collect()
    {
        PlugInToken? token = PlugInRegistry.Register();
        Assert.That(token, Is.Not.Null);

        Agent? agent = Agent.GetDeepSeekAgent(token);
        Assert.That(agent, Is.Not.Null);

        int count = 100;

        PlugInStatistics.Collect(agent, token, [new MessageTurn("", tokenCount: count)]);

        PlugInStatistics? stats = PlugInStatistics.Usage(token);
        Assert.That(stats, Is.Not.Null);

        Assert.That(stats.TotalTokenUsage, Is.EqualTo(count));
    }
    
    [Test]
    public void Serialize()
    {
        PlugInToken? token = PlugInRegistry.Register();
        Assert.That(token, Is.Not.Null);

        Agent? agent = Agent.GetDeepSeekAgent(token);
        Assert.That(agent, Is.Not.Null);

        int count = 100;

        PlugInStatistics.Collect(agent, token, [new MessageTurn("", tokenCount: count)]);
        int total = PlugInStatistics.Usage(token)?.TotalTokenUsage ?? 0;

        string json = AIInstances.SerializeToJson();
        Assert.That(AIInstances.LoadFromJson(json, out Exception? ex), Is.True, ex?.ToString());

        Assert.That(PlugInStatistics.Usage(token)?.TotalTokenUsage, Is.EqualTo(total));
    }

}
