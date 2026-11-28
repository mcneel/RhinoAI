using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;
using System.Diagnostics;

namespace sdk.tests;

public class RhinoHarnessTests
{

    private static Rhino.AI.PlugIns.PlugInToken Token => Rhino.AI.PlugIns.PlugInToken.Invalid;

    [TestCase("Create a script to generate a box in Rhino", "This task is a coding task")]
    [TestCase("Create a sphere with a radius of 5 at the origin", "The user wants you to create geometry")]
    [TestCase("Generate a photorealistic image of a wooden chair", "The user wants you to generate an image")]
    [TestCase("Build a Grasshopper definition that divides a curve into 10 points", "The user wants you to work with Grasshopper")]
    public async Task Classification(string prompt, string pass)
    {
        RhinoHarness harness = new (Token);
        Agent? agent = Agent.GetDeepSeekAgent(Token);
        agent = agent?.WithHarness(harness);
        Assert.That(agent, Is.Not.Null);

        IEnumerable<ITurn> turns = await harness.ClassifyRequest(agent, [new MessageTurn(prompt, RoleType.User)], default);
        MessageTurn choice = turns.OfType<MessageTurn>().Single();
        Assert.That(choice.Data.Contains(pass, StringComparison.InvariantCultureIgnoreCase));
    }

    [TestCase("What is the capital of France?")]
    public async Task ClassificationOther(string prompt)
    {
        RhinoHarness harness = new (Token);
        Agent? agent = Agent.GetDeepSeekAgent(Token);
        agent = agent?.WithHarness(harness);
        Assert.That(agent, Is.Not.Null);

        IEnumerable<ITurn> turns = await harness.ClassifyRequest(agent, [new MessageTurn(prompt, RoleType.User)], default);
        Assert.That(turns.OfType<MessageTurn>(), Is.Empty);
    }

}
