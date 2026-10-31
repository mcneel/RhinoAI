using System.Runtime.CompilerServices;
using Rhino.AI;
using Rhino.AI.Models;

namespace sdk.tests;

public class SkillTests
{
    private static Rhino.AI.PlugIns.PlugInToken Token => Rhino.AI.PlugIns.PlugInToken.Invalid;

    private sealed class RecordingModel : IModel
    {
        public string Name => "recording";

        public string Vendor => "test";

        public bool Available => true;

        public List<IReadOnlyList<ITurn>> Received { get; } = [];

        public async IAsyncEnumerable<ITurn> StreamAsync(Harness harness, IEnumerable<ITurn> turn, [EnumeratorCancellation]CancellationToken token)
        {
            Received.Add(turn.ToList());
            yield return new MessageTurn("ok", RoleType.Assistant);
        }
        public Task<IEnumerable<ITurn>> SendAsync(Harness harness, IEnumerable<ITurn> turn, CancellationToken token)
        {
            Received.Add(turn.ToList());
            return Task.FromResult<IEnumerable<ITurn>>([new MessageTurn("ok", RoleType.Assistant)]);
        }
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Assert.That(AIInstances.Agents.LoadAgentsFromJson(), Is.True);
    }


    [Test, CancelAfter(5000)]
    public async Task SkillsAreListedOnce(CancellationToken token)
    {
        RecordingModel model = new();

        TestDefinition definition = new ();
        definition.Add(model);
        
        AIInstances.Agents.Add(definition);

        Agent? agent = definition.GetAgent(Token);
        Assert.That(agent, Is.Not.Null);
        agent.Harness.AddSkill(new Skill("boxes", "How to draw boxes", "Use the Box command."));

        await agent.SendAsync("first", token);
        await agent.SendAsync("second", token);

        List<SystemTurn> systemTurns = model.Received[1].OfType<SystemTurn>().ToList();
        Assert.That(systemTurns, Has.Count.EqualTo(1));
        Assert.That(systemTurns[0].Prompt, Does.Contain("- boxes: How to draw boxes"));
    }

    [Test, CancelAfter(5000)]
    public async Task SimpleSkill(CancellationToken token)
    {
        Agent? agent = Agent.GetDeepSeekAgent(Token);
        Assert.That(agent, Is.Not.Null);

        agent.Harness.AddSkill(new Skill("Smoofle", "Teaches you about a Smoofle.", "A Smoofle is a large orange chicken with wheels."));

        IAgentResult result = await agent.SendAsync("What is a Smoofle?", token);
        Assert.That(result, Replied.With("large", "orange", "chicken", "wheels"));
    }

    [Test, CancelAfter(5000)]
    public async Task ReadSkillReturnsSkillData(CancellationToken token)
    {
        Agent? agent = Agent.GetDeepSeekAgent(Token);
        Assert.That(agent, Is.Not.Null);

        Harness harness = new(Token);
        harness.AddSkill(new Skill("boxes", "How to draw boxes", "Use the Box command."));

        ToolReturn result = await harness.UseToolAsync(agent, "Default_Tools", "read_skill", [new ToolString("name", "Boxes")], token);

        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
        Assert.That(result.Message, Is.EqualTo("Use the Box command."));
    }

    [Test]
    public void DuplicateSkillIsRejected()
    {
        Harness harness = new(Token);
        Assert.That(harness.AddSkill(new Skill("boxes", "a", "a")), Is.True);
        Assert.That(harness.AddSkill(new Skill("Boxes", "b", "b")), Is.False);
    }

}
