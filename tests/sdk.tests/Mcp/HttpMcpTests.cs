using Rhino.AI;

namespace sdk.tests;

public class HttpMcpTests
{

    private TestMcpServer? Server { get; set; }

    private Uri Url => Server?.Url ?? throw new InvalidOperationException("Test MCP server has not started.");

    [OneTimeSetUp]
    public async Task StartServerAsync()
    {
        Server = await TestMcpServer.StartAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task StopServerAsync()
    {
        if (Server is not null)
            await Server.DisposeAsync();
    }

    [Test, Ignore("HttpMcp is not implemented yet")]
    [CancelAfter(5000)]
    public async Task InitTest(CancellationToken token)
    {
        HttpMcp mcp = new ("Test", Url);
        Assert.That(await mcp.InitAsync(token));

        Assert.That(mcp.Tools, Is.Not.Empty);
    }

    [Test, Ignore("HttpMcp is not implemented yet")]
    [CancelAfter(5000)]
    public async Task ToolTest(CancellationToken token)
    {
        HttpMcp mcp = new ("Test", Url);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("ping", [], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
    }

    [Test, Ignore("HttpMcp is not implemented yet")]
    [CancelAfter(5000)]
    public async Task ToolTestBadArgs(CancellationToken token)
    {
        HttpMcp mcp = new ("Test", Url);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("repeat", [new ToolBoolean("asdsd", false)], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Failure));
    }

    [Test, Ignore("HttpMcp is not implemented yet")]
    [CancelAfter(5000)]
    public async Task NonExistantToolTest(CancellationToken token)
    {
        HttpMcp mcp = new ("Test", Url);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("buy_expensive_cheese", [], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Failure));
    }

    [Test, Ignore("HttpMcp is not implemented yet")]
    [CancelAfter(5000)]
    public async Task RegisterToolFails(CancellationToken token)
    {
        HttpMcp mcp = new ("Test", Url);
        Assert.That(mcp.RegisterTool(null!), Is.False);
    }

}
