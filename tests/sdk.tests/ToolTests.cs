using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;

namespace sdk.tests;

public class ToolTests
{

    private static Rhino.AI.PlugIns.PlugInToken Token => Rhino.AI.PlugIns.PlugInToken.Invalid;

    [Test, CancelAfter(5000)]
    public async Task ReadWriteToolSuccess(CancellationToken token)
    {
        string filePath = Path.Combine(Path.GetTempPath(), "tmp", "file.ext");

        {
            WriteTool write = new();

            IToolArg[] args = [
                new ToolPath("file", filePath),
                new ToolString("data", "shmoople")
            ];

            ToolReturn result = await write.UseAsync(args, token);

            Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
            Assert.That(result.Guidance, Is.Null.Or.Empty);
            Assert.That(result.Message, Is.Not.Empty);

            string dataIn = File.ReadAllText(filePath);
            Assert.That(dataIn, Is.EqualTo("shmoople"));
        }

        {
            ReadTool read = new();

            IToolArg[] args = [
                new ToolPath("file", filePath),
                new ToolString("data", "shmoople")
            ];

            ToolReturn result = await read.UseAsync(args, token);

            Assert.That(result.Message, Is.EqualTo("shmoople"));

            Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
            Assert.That(result.Guidance, Is.Null.Or.Empty);
            Assert.That(result.Message, Is.Not.Empty);
        }
    }

    [Test, CancelAfter(5000)]
    public async Task WriteToolNoArgs(CancellationToken token)
    {
        WriteTool tool = new();
        ToolReturn result = await tool.UseAsync([], token);

        Assert.That(result.Result, Is.EqualTo(ToolResult.Failure));
        Assert.That(result.Guidance, Is.Not.Empty);
        Assert.That(result.Message, Is.Not.Empty);
    }

    [Test, CancelAfter(10_000)]
    public async Task simpleDelegation(CancellationToken token)
    {
        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new(Token);
        Agent agent = new(Token, deepSeek, harness);

        IEnumerable<ITurn> turns = await agent.SendAsync($"Spawn a {DeepSeekModel.Default().Name} subagent to write a letter of resignation from its job as a subagent", token);
        Assert.That(turns, Turns.EndsWith("resign"));
    }

    [Test, CancelAfter(10_000)]
    public async Task FuzzyDelegation(CancellationToken token)
    {
        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new(Token);
        Agent agent = new(Token, deepSeek, harness);

        IEnumerable<ITurn> turns = await agent.SendAsync($"Spawn a subagent to write a letter of resignation from its job as a subagent", token);
        Assert.That(turns, Turns.EndsWith("resign"));
    }

}
