using Rhino.AI;

namespace sdk.tests;

public class StdioMcpTests
{

    private static string Server { get; } = Path.Combine(
        AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "StdioTestServer.exe" : "StdioTestServer");

    [Test]
    [CancelAfter(5000)]
    public async Task InitTest(CancellationToken token)
    {
        using StdioMcp mcp = new ("Test", Server);
        Assert.That(await mcp.InitAsync(token));

        Assert.That(mcp.Tools, Is.Not.Empty);
    }

    [Test]
    [CancelAfter(5000)]
    public async Task ToolTest(CancellationToken token)
    {
        using StdioMcp mcp = new ("Test", Server);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("ping", [], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
        Assert.That(result.Message, Is.EqualTo("pong"));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task ToolTestArgs(CancellationToken token)
    {
        using StdioMcp mcp = new ("Test", Server);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("repeat", [new ToolString("text", "cheese"), new ToolInt("count", 3)], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
        Assert.That(result.Message, Is.EqualTo("cheese cheese cheese"));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task ToolTestBadArgs(CancellationToken token)
    {
        using StdioMcp mcp = new ("Test", Server);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("repeat", [new ToolBoolean("asdsd", false)], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Failure));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task NonExistantToolTest(CancellationToken token)
    {
        using StdioMcp mcp = new ("Test", Server);
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("buy_expensive_cheese", [], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Failure));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task ServerStartsWithArgsAndEnv(CancellationToken token)
    {
        using StdioMcp mcp = new("Test", Server, ["first", "two words", "with \"quotes\""], new Dictionary<string, string> { ["STDIO_TEST_VALUE"] = "from env" });
        Assert.That(await mcp.InitAsync(token));

        ToolReturn result = await mcp.RunToolAsync("launch", [], token);
        Assert.That(result.Result, Is.EqualTo(ToolResult.Success));
        Assert.That(result.Message, Is.EqualTo("first|two words|with \"quotes\";from env"));
    }

    [Test]
    public void RegisterToolFails()
    {
        using StdioMcp mcp = new ("Test", Server);
        Assert.That(mcp.RegisterTool(null!), Is.False);
    }

}
