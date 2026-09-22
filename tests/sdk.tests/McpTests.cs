using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;

namespace sdk.tests;

public class McpTests
{

    private Agent Agent { get; }

    private CancellationTokenSource Source => new(10_000);

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

    [Test]
    public async Task NoArgs()
    {
        TestTool tool = new("Bloogle", "Tells you what the Bloogle is", [])
        {
            Func = async (a, t) => ToolReturn.Success("The Bloogle is a small red cat")
        };
        Mcp.RegisterTool(tool);

        IEnumerable<ITurn> turns = await Agent.SendAsync("Hello! What is the bloogle?", Source.Token);

        Assert.That(turns.Any(t => t is ToolTurn));
        Assert.That(turns.Any(t => t is ToolResultTurn));
        AssertTurns(turns.Last(), "cat", "small", "red");
    }

    [Test]
    public async Task OneOptionalArg()
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

        IEnumerable<ITurn> turns = await Agent.SendAsync("Hello! What is the bloogle?", Source.Token);

        Assert.That(turns.Any(t => t is ToolTurn));
        Assert.That(turns.Any(t => t is ToolResultTurn));
        AssertTurns(turns.Last(), "cat", "small", "red");
    }

    [Test]
    public async Task OneRequiredArg()
    {
        TestTool tool = new("Bloogle", "Tells you what the Bloogle is", [new ToolArg("Shmargle Arg", "Pass Shmargle as a string", ToolArgType.String, true)])
        {
            Func = async (a, t) =>
            {
                Assert.That(a, Is.Not.Empty);
                return ToolReturn.Success("The Bloogle is a small red cat");
            }
        };
        Mcp.RegisterTool(tool);

        IEnumerable<ITurn> turns = await Agent.SendAsync("Hello! What is the bloogle?", Source.Token);

        Assert.That(turns.Any(t => t is ToolTurn));
        Assert.That(turns.Any(t => t is ToolResultTurn));
        AssertTurns(turns.Last(), "cat", "small", "red");
    }

    [TestCase(ToolArgType.String, typeof(IToolString))]
    [TestCase(ToolArgType.URL, typeof(IToolUrl))]
    [TestCase(ToolArgType.FilePath, typeof(IToolPath))]
    [TestCase(ToolArgType.Number, typeof(IToolNumber))]
    [TestCase(ToolArgType.Integer, typeof(IToolInt))]
    [TestCase(ToolArgType.Boolean, typeof(IToolBoolean))]
    // [TestCase(ToolArgType.Array, typeof(IToolArray))]
    // [TestCase(ToolArgType.Object, typeof(IToolObject))]
    public async Task StringArg(ToolArgType arg, Type argType)
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

        IEnumerable<ITurn> turns = await Agent.SendAsync("Hello! What is the bloogle?", Source.Token);

        Assert.That(turns.Any(t => t is ToolTurn));
        Assert.That(turns.Any(t => t is ToolResultTurn));
        AssertTurns(turns.Last(), "cat", "small", "red");
    }

    private static void AssertTurns(ITurn turn, params string[] keywords)
    {
        if (turn is not MessageTurn message)
        {
            Assert.Fail("Last turn is not a MessageTurn");
            return;
        }
        if (MessageContains(message.Message, keywords)) return;
    }

    private static bool MessageContains(string message, string[] keywords)
    {
        foreach (string keyword in keywords)
        {
            if (!message.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
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
