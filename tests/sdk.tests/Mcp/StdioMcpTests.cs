using Rhino.AI;

namespace sdk.tests;

public class StdioMcpTests
{

    // NOTE : This will likely fail on CI as the mcp is not copied there. This test should do that.

    [Test]
    [CancelAfter(5000)]
    public async Task InitTest(CancellationToken token)
    {
        RhinoMcp mcp = new ("Rhino");
        Assert.That(await mcp.InitAsync(token));

        Assert.That(mcp.Tools, Is.Not.Empty);
    }

    [Test]
    [CancelAfter(5000)]
    public async Task ToolTest(CancellationToken token)
    {
        RhinoMcp mcp = new ("Rhino");
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("list_slots", [], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task NonExistantToolTest(CancellationToken token)
    {
        RhinoMcp mcp = new ("Rhino");
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("buy_expensive_cheese", [], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Failure));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task RegisterToolFails(CancellationToken token)
    {
        RhinoMcp mcp = new ("Rhino");
        Assert.That(mcp.RegisterTool(null!), Is.False);
    }

}
