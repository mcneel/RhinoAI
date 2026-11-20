using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;
using System.Diagnostics;

namespace sdk.tests;

public class AgentTests
{

    private static Rhino.AI.PlugIns.PlugInToken Token => Rhino.AI.PlugIns.PlugInToken.Invalid;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Assert.That(AIInstances.Agents.LoadAgentsFromJson(), Is.True);
    }

    [TestCase(ProtocolKind.Completions)]
    [TestCase(ProtocolKind.Messages)]
    [TestCase(ProtocolKind.Responses)]
    [CancelAfter(5000)]
    public async Task DeepSeekApi(ProtocolKind protocol, CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        Agent? agent = Agent.GetDeepSeekAgent(Token);
        Assert.That(agent, Is.Not.Null);

        agent.Harness.AddMcp(mcp);

        IAgentResult result = await agent.SendAsync("Hello! What is the weather today in Florida?", token);

        AssertUsedWeatherTool(result);
        Assert.That(result.TokenCount, Is.GreaterThan(0));
    }

    [Test, CancelAfter(5000)]
    public async Task OneCallAtATime(CancellationToken token)
    {
        Agent? agent = Agent.GetDeepSeekAgent(Token);
        Assert.That(agent, Is.Not.Null);

        await using IAsyncEnumerator<ITurn> first = agent.StreamAsync("Hello!", token).GetAsyncEnumerator(token);
        Assert.That(await first.MoveNextAsync(), Is.True);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await agent.SendAsync("Hello!", token));
    }

    [Test, CancelAfter(5000)]
    public async Task Denied(CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        Agent? agent = Agent.GetDeepSeekAgent(Token);
        Assert.That(agent, Is.Not.Null);
        
        agent.Harness.AddMcp(mcp);

        agent.Harness.AskUser += async (e, __) => { ((PermissionRequest)e).HasPermission = false; };

        IAgentResult result = await agent.SendAsync("Hello! What is the weather today in Florida?", token);
    }

    [Test, Category("Manual")]
    [CancelAfter(60_000)]
    public async Task LocalModelApi(CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        HttpAgentDefinition def = new ("LM Studio", "")
        {
            CurrentModelId = "qwen/qwen3-1.7b",
            Protocol = ProtocolKind.Default,
            Host = "http://localhost:1234",
            ModelsRoute = "/v1/models",
        };
        AIInstances.Agents.Add(def);
        def.Add(LocalModel.Completions(def.CurrentModelId, new Uri(def.Host)));
        Agent? agent = def.GetAgent(Token);
        Assert.That(agent, Is.Not.Null);

        agent.Harness.AddMcp(mcp);

        IAgentResult result = await agent.SendAsync("Hello! What is the weather today in Tampa Florida?", token);

        AssertUsedWeatherTool(result);
    }

    [Test, Category("Manual")]
    [CancelAfter(60_000)]
    public async Task CustomLocalModelApi(CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        ModelDefinition modelDefinition = new("LM Studio", "Qwen 3-1.7b", "qwen/qwen3-1.7b");
        HttpAgentDefinition definition = new("LM Studio", "LM Studio Community") { Protocol = ProtocolKind.Completions, Host = "http://localhost:1234", ModelsRoute = "/v1/models" };
        definition.Add(modelDefinition);
        definition.CurrentModelId = modelDefinition.ModelId;

        Assert.That(AIInstances.Agents.Add(definition));

        Agent? agent = Agent.GetCustomAgent(definition, Token);
        Assert.That(agent, Is.Not.Null);
        agent.Harness.AddMcp(mcp);

        IAgentResult result = await agent.SendAsync("Hello! What is the weather today in Tampa Florida?", token);

        AssertUsedWeatherTool(result);
    }

    [Test, Category("Manual")]
    [CancelAfter(60_000)]
    public async Task DesktopClaude(CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        Agent? agent = Agent.GetClaudeDesktopAgent(Token);
        Assert.That(agent, Is.Not.Null);
        agent.Harness.AddMcp(mcp);

        ClaudeHarness harness = (ClaudeHarness)agent.Harness;
        harness.AskUser += async (e, _) =>
        {
            Assert.That(e.HasPermission, Is.False);
            if (e is PermissionRequest pr) pr.HasPermission = true;
            Assert.That(e.HasPermission);
        };

        IAgentResult result = await agent.SendAsync("Hello! What is the weather today in Tampa Florida?", token);

        AssertUsedWeatherTool(result);
    }

    [Test, Category("Manual")]
    [CancelAfter(60_000)]
    public async Task DesktopCodex(CancellationToken token)
    {
        MemoryMcp mcp = new("Weather MCP");

        WeatherTool tool = new();
        mcp.RegisterTool(tool);

        Agent? agent = Agent.GetCodexDesktopAgent(Token);
        Assert.That(agent, Is.Not.Null);
        agent.Harness.AddMcp(mcp);

        CodexHarness harness = (CodexHarness)agent.Harness;
        harness.AskUser += async (e, _) =>
        {
            Assert.That(e.HasPermission, Is.False);
            if (e is PermissionRequest pr) pr.HasPermission = true;
            Assert.That(e.HasPermission);
        };

        IAgentResult result = await agent.SendAsync("Hello! What is the weather today in Tampa, Florida?", token);

        AssertUsedWeatherTool(result);
    }

    [TestCase("--resume;cheese")]
    [TestCase("--resume;3059787b-db32-48ba-a85a-9ad9a41c212b")]
    [TestCase("--cheese")]
    [Category("Manual")]
    [CancelAfter(60_000)]
    public async Task DesktopClaudeErrors(string args, CancellationToken token)
    {
        Process claudeProcess = GetClaudeProcess();
        foreach(string arg in args.Split(';'))
        {
            claudeProcess.StartInfo.ArgumentList.Add(arg);
        }
        AIProcess process = new(claudeProcess);

        claudeProcess.Start();
        claudeProcess.BeginErrorReadLine();
        claudeProcess.StandardInput.Close();

        await claudeProcess.WaitForExitAsync(token);

        Assert.That(process.TryPop<MessageTurn>(out MessageTurn turn));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task ModelEffort(CancellationToken token)
    {
        Agent? agent = Agent.GetDeepSeekAgent(Token);
        Assert.That(agent, Is.Not.Null);

        agent.Config.ModelEffort = "max";

        IAgentResult result = await agent.SendAsync("What effort level are you using?", token);
        // TODO : Assert a message contains "max" and not "low" etc.
    }

    private static Process GetClaudeProcess()
    {
        ClaudeDesktopModel desktopModel = (ClaudeDesktopModel)AIInstances.Agents.FromParameters("Anthropic", AgentType.Cli)?.GetModel("opus")!;
        string exePath = desktopModel.ExePath;

        Process process = new()
        {
            EnableRaisingEvents = true,
            StartInfo = new()
            {
                RedirectStandardError = true,
                RedirectStandardInput = true,

                FileName = exePath,
                WorkingDirectory = Path.GetTempPath(),
                CreateNoWindow = true,
            }
        };
        process.StartInfo.ArgumentList.Add("-p");

        return process;
    }

    private static void AssertUsedWeatherTool(IAgentResult result)
    {
        Assert.That(result.Reason, Is.EqualTo(StopReason.EndTurn));

        ToolCall? weather = result.ToolCalls.FirstOrDefault(c => c.Name.EndsWith("get_weather"));
        Assert.That(weather, Is.Not.Null, "The model never called the weather tool.");
        Assert.That(weather?.Success, Is.True, "The weather tool's result never came back.");

        Assert.That(result.Text, Is.Not.Null, "The model never answered.");
        Assert.That(result.Text, Does.Contain("stormy").IgnoreCase, "The answer does not use the weather tool's result.");

        Assert.That(result, Has.All.Matches<ITurn>(t => t.Success), "A turn in the transcript failed.");
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
