namespace Rhino.AI.Mcps;

internal abstract record McpParseResult
{

    private McpParseResult() { }

    public sealed record Parsed(McpRequest Request) : McpParseResult;

    public sealed record Rejected(McpResponse Response) : McpParseResult;

}
