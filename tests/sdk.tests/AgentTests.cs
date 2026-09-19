using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;

namespace sdk.tests;

public class AgentTests
{

    [Test]
    public async Task GeminiApi()
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        GeminiModel gemini = new ("gemini-3.5-flash-lite", "Google");
        GenericHarness harness = new();
        harness.AddMcp(mcp);
        Agent agent = new(gemini, harness);

        CancellationTokenSource source = new(100_000);
        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Florida?", source.Token);
    }

    [Test]
    public async Task Denied()
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        GeminiModel gemini = new ("gemini-3.5-flash-lite", "Google");
        GenericHarness harness = new();
        harness.AddMcp(mcp);
        Agent agent = new(gemini, harness);

        harness.PermissionRequested += (_, e) => e.HasPermission = false;

        CancellationTokenSource source = new(100_000);
        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Florida?", source.Token);
    }

    [Test]
    public async Task DesktopClaude()
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        // TODO : How to add an MCP?
        Agent agent = Agent.GetClaudeDesktopAgent();
        agent.Harness.AddMcp(mcp);

        CancellationTokenSource source = new(100_000);
        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Florida?", source.Token);
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
