using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;

namespace sdk.tests;

public class AgentTests
{

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
        GenericHarness harness = new();
        harness.AddMcp(mcp);
        Agent agent = new(deepSeek, harness);

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
        GenericHarness harness = new();
        harness.AddMcp(mcp);
        Agent agent = new(deepSeek, harness);

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
        GenericHarness harness = new();
        harness.AddMcp(mcp);
        Agent agent = new(lmStudio, harness);

        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Florida?", token);
    }

    [Test, Category("Manual")]
    [CancelAfter(5000)]
    public async Task DesktopClaude(CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        Agent agent = Agent.GetClaudeDesktopAgent();
        agent.Harness.AddMcp(mcp);

        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Florida?", token);
        ;
    }

    private class WeatherTool : ITool
    {
        public string Name => "get_weather";

        public string Description => "Gets the current weather";

        public bool ReadOnly => true;

        public bool Destructive => false;

        public ToolArg[] Args { get; } = [
          new ToolArg("city", "The City to check", ToolArgType.String, true)  
        ];

        public async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
        {
            if (!args.TryGetString("city", out string city)) return ToolReturn.Failure("City parameter is mandatory", "Please include the City parameter");
            return ToolReturn.Success($"The weather is 18 degrees and extremely stormy in {city}");
        }

    }

}
