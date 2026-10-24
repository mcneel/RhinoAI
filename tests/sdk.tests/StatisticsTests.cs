using Rhino.AI;
using Rhino.AI.PlugIns;
using Rhino.AI.Statistics;

namespace sdk.tests;

public class StatisticsTests
{
    
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Assert.That(Instances.Agents.LoadAgentsFromJson(), Is.True);
    }

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

        string? json = PlugInStatistics.GetJson();
        Assert.That(json, Is.Not.Null);

        Assert.That(PlugInStatistics.LoadFromJson(json));
    }

}
