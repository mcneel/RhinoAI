using Rhino.AI;
using Rhino.AI.Tools;

namespace sdk.tests;

public class Tests
{
    [SetUp]
    public void Setup()
    {

    }

    [Test]
    public async Task Test1()
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        Model gemini = new ("gemini-3.5-flash-lite", "Google");
        RhinoHarness harness = new();
        harness.AddMcp(mcp);
        Agent agent = new(gemini, harness);

        CancellationTokenSource source = new(100_000);
        IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Florida?", source.Token);
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
