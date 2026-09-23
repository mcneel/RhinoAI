using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;

namespace sdk.tests;

public class McpTests
{

    private Agent Agent { get; }

    private MemoryMcp Mcp { get; } = new("AnyMCP");

    public McpTests()
    {
        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new();
        harness.AddMcp(Mcp);
        Agent = new(deepSeek, harness);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Mcp.Dispose();
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

        IEnumerable<ITurn> turns = await Agent.SendAsync("Hello! What is the bloogle?", token);

        Assert.That(turns.Any(t => t is ToolTurn));
        Assert.That(turns.Any(t => t is ToolResultTurn));
        Assert.That(turns, Turns.EndsWith("cat", "small", "red"));
    }

    [Test, CancelAfter(5000)]
    public async Task OneOptionalArg(CancellationToken token)
    {
        TestTool tool = new("Bloogle", "Tells you what the Bloogle is", [new ToolArg("Pointless Arg", "DO NOT USE THIS ITS BAD", ToolArgType.String, false)])
        {
            Func = async (a, t) =>
            {
                Assert.That(a, Is.Empty);
                return ToolReturn.Success("The Bloogle is a small red cat");
            }
        };
        Mcp.RegisterTool(tool);

        IEnumerable<ITurn> turns = await Agent.SendAsync("Hello! What is the bloogle?", token);

        Assert.That(turns.Any(t => t is ToolTurn));
        Assert.That(turns.Any(t => t is ToolResultTurn));
        Assert.That(turns, Turns.EndsWith("cat", "small", "red"));
    }

    [Test, CancelAfter(5000)]
    public async Task OneRequiredArg(CancellationToken token)
    {
        TestTool tool = new("Bloogle", "Tells you what the Bloogle is", [new ToolArg("shmargle", "Pass shmargle as a string", ToolArgType.String, true)])
        {
            Func = async (a, t) =>
            {
                Assert.That(a, Is.Not.Empty);
                return ToolReturn.Success("The Bloogle is a small red cat");
            }
        };
        Mcp.RegisterTool(tool);

        IEnumerable<ITurn> turns = await Agent.SendAsync("Hello! What is the bloogle?", token);

        Assert.That(turns.Any(t => t is ToolTurn));
        Assert.That(turns.Any(t => t is ToolResultTurn));
        Assert.That(turns, Turns.EndsWith("cat", "small", "red"));
    }

    [TestCase(ToolArgType.String, typeof(IToolString))]
    [TestCase(ToolArgType.URL, typeof(IToolUrl))]
    [TestCase(ToolArgType.FilePath, typeof(IToolPath))]
    [TestCase(ToolArgType.Number, typeof(IToolNumber))]
    [TestCase(ToolArgType.Integer, typeof(IToolInt))]
    [TestCase(ToolArgType.Boolean, typeof(IToolBoolean))]
    // [TestCase(ToolArgType.Array, typeof(IToolArray))]
    // [TestCase(ToolArgType.Object, typeof(IToolObject))]
    [CancelAfter(5000)]
    public async Task StringArg(ToolArgType arg, Type argType, CancellationToken token)
    {
        TestTool tool = new("Bloogle", "Tells you what the Bloogle is", [new ToolArg("Shmargle Arg", $"Pass Shmargle as a {arg}", arg, true)])
        {
            Func = async (a, t) =>
            {
                Assert.That(a[0].GetType(), Is.EqualTo(argType));
                return ToolReturn.Success("The Bloogle is a small red cat");
            }
        };
        Mcp.RegisterTool(tool);

        IEnumerable<ITurn> turns = await Agent.SendAsync("Hello! What is the bloogle?", token);

        Assert.That(turns.Any(t => t is ToolTurn));
        Assert.That(turns.Any(t => t is ToolResultTurn));
        Assert.That(turns, Turns.EndsWith("cat", "small", "red"));
    }

    [Test, CancelAfter(5000)]
    public async Task FuzzyTool(CancellationToken token)
    {
        GenericHarness harness = new();

        TestTool tool = new("read", "", []);
        Mcp.RegisterTool(tool);
        harness.AddMcp(Mcp);

        ToolReturn result = await harness.UseToolAsync("AnyMCP", "reed", [], token);
        Assert.That(result.Guidance, Does.Contain(tool.Name));
        Assert.That(result.Result == ToolResult.Failure);
    }

    private record TestTool(string Name, string Description, ToolArg[] Args) : ITool
    {
        public bool ReadOnly => true;

        public bool Destructive => false;

        public Func<IReadOnlyList<IToolArg>, CancellationToken, Task<ToolReturn>>? Func { get; set; }

        public async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
        {
            Task<ToolReturn>? task = (Func?.Invoke(args, token)) ?? throw new NotImplementedException("Misshing Func!");
            return await task;
        }

    }

}
