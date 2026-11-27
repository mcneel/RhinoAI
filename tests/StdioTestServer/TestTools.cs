using ModelContextProtocol.Server;

namespace StdioTestServer;

[McpServerToolType]
public sealed class TestTools
{

    [McpServerTool(Name = "ping")]
    public static string Ping() => "pong";

    [McpServerTool(Name = "repeat")]
    public static string Repeat(string text, int count, string separator = " ")
        => string.Join(separator, Enumerable.Repeat(text, count));

    [McpServerTool(Name = "launch")]
    public static string Launch()
        => $"{string.Join("|", Environment.GetCommandLineArgs().Skip(1))};{Environment.GetEnvironmentVariable("STDIO_TEST_VALUE")}";

}
