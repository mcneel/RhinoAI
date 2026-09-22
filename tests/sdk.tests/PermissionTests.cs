using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;

namespace sdk.tests;

public class PermissionTests
{

    [Test]
    public async Task PermissionRequested()
    {
        bool permissionRequested = false;
        DeepSeekModel deepSeek = new("deepseek-chat");
        GenericHarness harness = new();
        Agent agent = new(deepSeek, harness);

        harness.PermissionRequested += (_, __) => permissionRequested = true;

        CancellationTokenSource source = new(10_000);
        IEnumerable<ITurn> turns = await agent.SendAsync("What is the value of PI?", source.Token);
        Assert.That(permissionRequested);
    }

    [Test]
    public async Task BlockedTool()
    {
        DeepSeekModel deepSeek = new("deepseek-chat");
        GenericHarness harness = new();
        Agent agent = new(deepSeek, harness);

        MemoryMcp mcp = new ("Smoople");
        mcp.RegisterTool(new TestUtils.TestTool("Smoople", "The Smoople Tool", []));
        harness.AddMcp(mcp);

        Assert.That(harness.Permissions.AddPermission("Smoople", new Permission("Smoople", Permissability.Deny, [])));

        CancellationTokenSource source = new(100_000);
        IEnumerable<ITurn> turns = await agent.SendAsync("Please run the Smoople tool", source.Token);
        Assert.That(turns.Any(t => t is ToolResultTurn result && result.Return.Result == ToolResult.Failure));
    }

}
