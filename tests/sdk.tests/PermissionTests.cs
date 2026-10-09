using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;

namespace sdk.tests;

public class PermissionTests
{
    
    private static Rhino.AI.PlugIns.PlugInToken Token => Rhino.AI.PlugIns.PlugInToken.Invalid;

    [Test, CancelAfter(5000)]
    public async Task PermissionRequested(CancellationToken token)
    {
        bool permissionRequested = false;
        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new(Token);
        Agent agent = new(Token, deepSeek, harness);

        MemoryMcp mcp = new("Smoople");
        mcp.RegisterTool(new TestUtils.TestTool("Smoople", "The Smoople Tool", [])
        {
            Func = (_, _) => Task.FromResult(ToolReturn.Success("Smoopled")),
        });
        harness.AddMcp(mcp);

        harness.AskUser += async (e, __) => e.HasPermission = permissionRequested = true;

        IEnumerable<ITurn> turns = await agent.SendAsync("Please run the Smoople tool", token);

        Assert.That(turns.Any(t => t is ToolTurn), "The model answered without calling a tool, so the permission gate was never reached.");
        Assert.That(permissionRequested);
    }

    [Test, CancelAfter(5000)]
    public async Task BlockedTool(CancellationToken token)
    {
        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new(Token);
        Agent agent = new(Token, deepSeek, harness);

        MemoryMcp mcp = new ("Smoople");
        TestUtils.TestTool tool = new ("Smoople", "The Smoople Tool", []);
        mcp.RegisterTool(tool);
        harness.AddMcp(mcp);

        Assert.That(UserSettings.Permissions.AddPermission(new Permission(mcp, tool, Permissability.Deny, [])));

        IEnumerable<ITurn> turns = await agent.SendAsync("Please run the Smoople tool", token);
        Assert.That(turns.Any(t => t is ToolResultTurn result && result.Return.Result == ToolResult.Failure));
    }

}
