using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;

namespace sdk.tests;

public class McpTests
{
    
    private static Rhino.AI.PlugIns.PlugInToken Token => Rhino.AI.PlugIns.PlugInToken.Invalid;

    private Agent Agent { get; }

    private MemoryMcp Mcp { get; } = new("AnyMCP");

    public McpTests()
    {
        Assert.That(AIInstances.Agents.LoadAgentsFromJson(), Is.True);

        Agent = Agent.GetDeepSeekAgent(Token)!;
        Agent.Harness.AddMcp(Mcp);

        Assert.That(Agent, Is.Not.Null);
    }

    [OneTimeTearDown]
    public void OnetimeTearDown()
    {
        Mcp.Dispose();
    }

    [TearDown]
    public void TearDown()
    {
        Agent.ClearConversation();
    }

    [SetUp]
    public void SetUp()
    {
        Mcp.ClearTools();
    }

    [Test, CancelAfter(5000)]
    public async Task NoArgs(CancellationToken token)
    {
        TestTool tool = new("Bloogle", "Tells you what the Bloogle is", [])
        {
            Func = async (a, t) => ToolReturn.Success("The Bloogle is a small red cat")
        };
        Mcp.RegisterTool(tool);

        IAgentResult result = await Agent.SendAsync("Hello! What is the bloogle?", token);

        Assert.That(result.ToolCalls.Any(c => c.Success));
        Assert.That(result, Replied.With("cat", "small", "red"));
    }

    [Test, CancelAfter(5000)]
    public async Task OneOptionalArg(CancellationToken token)
    {
        TestTool tool = new("Bloogle", "Tells you what the Bloogle is", [new ToolParameter("Pointless", "DO NOT USE THIS ITS BAD", ToolArgType.String, false)])
        {
            Func = async (a, t) =>
            {
                Assert.That(a, Is.Empty);
                return ToolReturn.Success("The Bloogle is a small red cat");
            }
        };
        Mcp.RegisterTool(tool);

        IAgentResult result = await Agent.SendAsync("Hello! What is the bloogle?", token);

        Assert.That(result.ToolCalls.Any(c => c.Success));
        Assert.That(result, Replied.With("cat", "small", "red"));
    }

    [Test, CancelAfter(5000)]
    public async Task OneRequiredArg(CancellationToken token)
    {
        TestTool tool = new("Bloogle", "Tells you what the Bloogle is", [new ToolParameter("shmargle", "Pass shmargle as a string", ToolArgType.String, true)])
        {
            Func = async (a, t) =>
            {
                Assert.That(a, Is.Not.Empty);
                return ToolReturn.Success("The Bloogle is a small red cat");
            }
        };
        Mcp.RegisterTool(tool);

        IAgentResult result = await Agent.SendAsync("Hello! What is the bloogle?", token);

        Assert.That(result.ToolCalls.Any(c => c.Success));
        Assert.That(result, Replied.With("cat", "small", "red"));
    }

    [TestCase(ToolArgType.String, typeof(ToolString))]
    [TestCase(ToolArgType.URL, typeof(ToolUrl))]
    [TestCase(ToolArgType.FilePath, typeof(ToolPath))]
    [TestCase(ToolArgType.Number, typeof(ToolNumber))]
    [TestCase(ToolArgType.Integer, typeof(ToolInt))]
    [TestCase(ToolArgType.Boolean, typeof(ToolBoolean))]
    // [TestCase(ToolArgType.Array, typeof(ToolArray))]
    // [TestCase(ToolArgType.Object, typeof(ToolObject))]
    [CancelAfter(5000)]
    public async Task StringArg(ToolArgType arg, Type argType, CancellationToken token)
    {
        TestTool tool = new("Bloogle", "Tells you what the Bloogle is", [new ToolParameter("Arg", $"Pass Shmargle as a {arg}", arg, true)])
        {
            Func = async (a, t) =>
            {
                Assert.That(a[0].GetType(), Is.EqualTo(argType));
                return ToolReturn.Success("The Bloogle is a small red cat");
            }
        };
        Mcp.RegisterTool(tool);

        IAgentResult result = await Agent.SendAsync("Hello! What is the bloogle?", token);

        Assert.That(result.ToolCalls.Any(c => c.Success));
        Assert.That(result, Replied.With("cat", "small", "red"));
    }

    [Test, CancelAfter(5000)]
    public async Task FuzzyTool(CancellationToken token)
    {
        Harness harness = new(Token);

        TestTool tool = new("read", "", []);
        Mcp.RegisterTool(tool);
        harness.AddMcp(Mcp);

        ToolReturn result = await harness.UseToolAsync(Agent, "AnyMCP", "reed", [], token);
        Assert.That(result.Guidance, Does.Contain(tool.Name));
        Assert.That(result.Result == ToolResult.Failure);
    }

    private record TestTool(string Name, string Description, ToolParameter[] Args) : Tool(Name, Description, true, false, Args)
    {

        public Func<IReadOnlyList<IToolArg>, CancellationToken, Task<ToolReturn>>? Func { get; set; }

        public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
        {
            Task<ToolReturn>? task = (Func?.Invoke(args, token)) ?? throw new NotImplementedException("Misshing Func!");
            return await task;
        }

    }

}
