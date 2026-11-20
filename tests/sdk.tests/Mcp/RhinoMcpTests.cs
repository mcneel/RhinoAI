using Rhino.AI;

namespace sdk.tests;

// Drives the router staged in the user's AppData by a Rhino install, which CI does not have.
[Category("Manual")]
public class RhinoMcpTests
{

    [Test]
    [CancelAfter(5000)]
    public async Task InitTest(CancellationToken token)
    {
        using RhinoMcp mcp = new ("Rhino");
        Assert.That(await mcp.InitAsync(token));

        Assert.That(mcp.Tools, Is.Not.Empty);
    }

    [Test]
    [CancelAfter(5000)]
    public async Task ToolTest(CancellationToken token)
    {
        using RhinoMcp mcp = new ("Rhino");
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("list_slots", [], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
    }

}
