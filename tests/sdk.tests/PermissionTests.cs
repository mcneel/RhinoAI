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
        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new();
        Agent agent = new(deepSeek, harness);

        MemoryMcp mcp = new("Smoople");
        mcp.RegisterTool(new TestUtils.TestTool("Smoople", "The Smoople Tool", [])
        {
            Func = (_, _) => Task.FromResult(ToolReturn.Success("Smoopled")),
        });
        harness.AddMcp(mcp);

        harness.PermissionRequested += (_, __) => permissionRequested = true;

        CancellationTokenSource source = new(100_000);
        IEnumerable<ITurn> turns = await agent.SendAsync("Please run the Smoople tool", source.Token);

        Assert.That(turns.Any(t => t is ToolTurn), "The model answered without calling a tool, so the permission gate was never reached.");
        Assert.That(permissionRequested);
    }

    [Test]
    public async Task BlockedTool()
    {
        DeepSeekModel deepSeek = DeepSeekModel.Default();
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
