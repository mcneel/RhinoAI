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

}
