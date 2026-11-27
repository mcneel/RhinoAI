using Rhino.AI;

namespace sdk.tests;

public class HttpMcpTests
{

    private TestMcpServer? Server { get; set; }

    private JsonMcpServer? JsonServer { get; set; }

    private Uri Url => Server?.Url ?? throw new InvalidOperationException("Test MCP server has not started.");

    private Uri JsonUrl => JsonServer?.Url ?? throw new InvalidOperationException("JSON MCP server has not started.");

    [OneTimeSetUp]
    public async Task StartServerAsync()
    {
        Server = await TestMcpServer.StartAsync(CancellationToken.None);
        JsonServer = await JsonMcpServer.StartAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task StopServerAsync()
    {
        if (Server is not null)
            await Server.DisposeAsync();

        if (JsonServer is not null)
            await JsonServer.DisposeAsync();
    }

    [Test]
    [CancelAfter(5000)]
    public async Task InitTest(CancellationToken token)
    {
        using HttpMcp mcp = new ("Test", Url);
        Assert.That(await mcp.InitAsync(token));

        Assert.That(mcp.Tools, Is.Not.Empty);
    }

    [Test]
    [CancelAfter(5000)]
    public async Task ToolTest(CancellationToken token)
    {
        using HttpMcp mcp = new ("Test", Url);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("ping", [], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
        Assert.That(result.Message, Is.EqualTo("pong"));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task ToolTestArgs(CancellationToken token)
    {
        using HttpMcp mcp = new ("Test", Url);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("repeat", [new ToolString("text", "cheese"), new ToolInt("count", 3)], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
        Assert.That(result.Message, Is.EqualTo("cheese cheese cheese"));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task ToolTestBadArgs(CancellationToken token)
    {
        using HttpMcp mcp = new ("Test", Url);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("repeat", [new ToolBoolean("asdsd", false)], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Failure));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task NonExistantToolTest(CancellationToken token)
    {
        using HttpMcp mcp = new ("Test", Url);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("buy_expensive_cheese", [], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Failure));
    }

    [Test]
    public void RegisterToolFails()
    {
        using HttpMcp mcp = new ("Test", Url);
        Assert.That(mcp.RegisterTool(null!), Is.False);
    }

    [Test]
    [CancelAfter(5000)]
    public async Task JsonServerToolTest(CancellationToken token)
    {
        using HttpMcp mcp = new ("Test", JsonUrl);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("echo", [new ToolString("text", "cheese")], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
        Assert.That(result.Message, Is.EqualTo("cheese"));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task HeadersAreSent(CancellationToken token)
    {
        using HttpMcp mcp = new ("Test", JsonUrl, new Dictionary<string, string> { ["Authorization"] = "Bearer cheese" });
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("authorization", [], token);
        Assert.That(result.Message, Is.EqualTo("Bearer cheese"));
    }

    [Test]
    [CancelAfter(5000)]
    public void UnreachableServerThrows(CancellationToken token)
    {
        using HttpMcp mcp = new ("Test", new Uri("http://127.0.0.1:1/mcp"));
        Assert.ThrowsAsync<InvalidOperationException>(() => mcp.InitAsync(token));
    }

}
