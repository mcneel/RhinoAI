using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;

namespace sdk.tests;

public class AgentTests
{

    private static Rhino.AI.PlugIns.PlugInToken Token => Rhino.AI.PlugIns.PlugInToken.Invalid;

    [TestCase("completions")]
    [TestCase("anthropic")]
    [TestCase("responses")]
    [CancelAfter(5000)]
    public async Task DeepSeekApi(string protocol, CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        DeepSeekModel deepSeek = protocol switch
        {
            "completions" => DeepSeekModel.Completions("deepseek-flash"),
            "anthropic" => DeepSeekModel.Anthropic("deepseek-flash"),
            "responses" => DeepSeekModel.Responses("deepseek-flash"),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
        };
        GenericHarness harness = new(Token);
        harness.AddMcp(mcp);
        Agent agent = new(Token, deepSeek, harness);

        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Florida?", token);

        Assert.That(turns.Any(t => t is ToolResultTurn), "The model answered without calling the weather tool.");
        Assert.That(turns.Last(), Is.InstanceOf<MessageTurn>());
    }

    [Test, CancelAfter(5000)]
    public async Task Denied(CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new(Token);
        harness.AddMcp(mcp);
        Agent agent = new(Token, deepSeek, harness);

        harness.PermissionRequested += (_, e) => e.HasPermission = false;

        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Florida?", token);
    }

    [Test, Category("Manual")]
    [CancelAfter(5000)]
    public async Task LMStudioApi(CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        LMStudioModel lmStudio = LMStudioModel.Default("qwen/qwen3-8b");
        GenericHarness harness = new(Token);
        harness.AddMcp(mcp);
        Agent agent = new(Token, lmStudio, harness);

        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Tampa Florida?", token);
        Assert.That(turns, Is.Not.Empty);
    }

    [Test, Category("Manual")]
    [CancelAfter(60_000)]
    public async Task DesktopClaude(CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        Agent agent = Agent.GetClaudeDesktopAgent(Token, "opus");
        agent.Harness.AddMcp(mcp);
        
        AllowAll(agent.Harness);

        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Tampa Florida?", token);

        AssertUsedWeatherTool(turns.ToList());
    }

    [Test, Category("Manual")]
    [CancelAfter(60_000)]
    public async Task DesktopCodex(CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        Agent agent = Agent.GetCodexDesktopAgent(Token, "gpt-6-astra");
        agent.Harness.AddMcp(mcp);

        AllowAll(agent.Harness);

        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Tampa, Florida?", token);

        AssertUsedWeatherTool(turns.ToList());
    }

    private static void AssertUsedWeatherTool(List<ITurn> transcript)
    {
        Assert.That(transcript, Is.Not.Empty);
        Assert.That(transcript.Last(), Is.InstanceOf<TurnEnd>().With.Property(nameof(TurnEnd.Reason)).EqualTo(StopReason.EndTurn));

        Assert.That(transcript.OfType<ToolTurn>().Any(t => t.Name.EndsWith("get_weather")), "The model never called the weather tool.");
        Assert.That(transcript.OfType<ToolResultTurn>().Any(r => r.Return.Result == ToolResult.Success), "The weather tool's result never came back.");

        Assert.That(transcript[^2], Is.InstanceOf<MessageTurn>());
        MessageTurn answer = (MessageTurn)transcript[^2];
        Assert.That(answer.Role, Is.EqualTo(RoleType.Assistant));
        Assert.That(answer.Message, Does.Contain("stormy").IgnoreCase, "The answer does not use the weather tool's result.");

        Assert.That(transcript, Has.All.Matches<ITurn>(t => t.Success), "A turn in the transcript failed.");
    }

    private static void AllowAll(IHarness harness)
    {
        foreach(IMcp mcp in harness.Mcps.Values)
        {
            harness.Permissions.AddPermission(new Permission(mcp, Permissability.Always, []));
        }
    }

    private sealed record WeatherTool() : Tool("get_weather", "Gets the current weather", true, false,
        [new ToolParameter("city", "The City to check", ToolArgType.String, true)])
    {

        public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
        {
            if (!args.TryGetString("city", out string city)) return ToolReturn.Failure("City parameter is mandatory", "Please include the City parameter");
            return ToolReturn.Success($"The weather is 18 degrees and extremely stormy in {city}");
        }

    }

}
