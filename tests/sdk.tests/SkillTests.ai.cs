using Rhino.AI;
using Rhino.AI.Models;

namespace sdk.tests;

public class SkillTests
{

    private sealed class RecordingModel : IModel
    {
        public string Name => "recording";

        public string Vendor => "test";

        public bool Available => true;

        public List<IReadOnlyList<ITurn>> Received { get; } = [];

        public Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turn, CancellationToken token)
        {
            Received.Add(turn.ToList());
            return Task.FromResult<IEnumerable<ITurn>>([new MessageTurn("ok", RoleType.Assistant)]);
        }
    }

    [Test, CancelAfter(5000)]
    public async Task SkillsAreListedOnce(CancellationToken token)
    {
        RecordingModel model = new();
        GenericHarness harness = new();
        harness.AddSkill(new Skill("boxes", "How to draw boxes", "Use the Box command."));
        Agent agent = new(model, harness, "Be helpful.");

        await agent.SendAsync("first", token);
        await agent.SendAsync("second", token);

        List<SystemTurn> systemTurns = model.Received[1].OfType<SystemTurn>().ToList();
        Assert.That(systemTurns, Has.Count.EqualTo(2));
        Assert.That(systemTurns[1].Prompt, Does.Contain("- boxes: How to draw boxes"));
    }

    [Test, CancelAfter(5000)]
    public async Task SimpleSkill(CancellationToken token)
    {
        DeepSeekModel model = DeepSeekModel.Default();
        GenericHarness harness = new();
        harness.AddSkill(new Skill("Smoofle", "Teaches you about a Smoofle.", "A Smoofle is a large orange chicken with wheels."));
        Agent agent = new(model, harness, "Be helpful.");

        IEnumerable<ITurn> turns = await agent.SendAsync("What is a Smoofle?", token);
        Assert.That(turns, Turns.EndsWith("large", "orange", "chicken", "wheels"));
    }

    [Test, CancelAfter(5000)]
    public async Task ReadSkillReturnsSkillData(CancellationToken token)
    {
        GenericHarness harness = new();
        harness.AddSkill(new Skill("boxes", "How to draw boxes", "Use the Box command."));

        ToolReturn result = await harness.UseToolAsync("Default Tools", "read_skill", [new ToolString("name", "Boxes")], token);

        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
        Assert.That(result.Message, Is.EqualTo("Use the Box command."));
    }

    [Test]
    public void DuplicateSkillIsRejected()
    {
        GenericHarness harness = new();
        Assert.That(harness.AddSkill(new Skill("boxes", "a", "a")), Is.True);
        Assert.That(harness.AddSkill(new Skill("Boxes", "b", "b")), Is.False);
    }

}
