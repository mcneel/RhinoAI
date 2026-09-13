using Rhino.AI;

namespace sdk.tests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
        
    }

    [Test]
    public async Task Test1()
    {
        Agent agent = new(new RhinoHarness());
        CancellationTokenSource source = new (10_000);
        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today?", source.Token);
    }

}
