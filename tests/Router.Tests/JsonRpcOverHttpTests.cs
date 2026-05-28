using System.Text.Json;
using RhMcp.Router.Resources;
using Xunit;

namespace RhMcp.Router.Tests;

// Pins the response-parsing contract for talking to child Rhino MCP endpoints.
// Both ProxyDispatcher (tool calls) and ResourceProxy (resource forwarding)
// route their responses through JsonRpcOverHttp.ExtractResult, so a regression
// here breaks both code paths.
public class JsonRpcOverHttpTests
{
    [Fact]
    public void Parses_bare_jsonrpc_response()
    {
        string body = """{"jsonrpc":"2.0","id":"abc","result":{"resources":[{"uri":"rhino://x","name":"x"}]}}""";

        JsonElement result = JsonRpcOverHttp.ExtractResult(body, slotId: "slot-1", operation: "resources/list");

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.True(result.TryGetProperty("resources", out JsonElement arr));
        Assert.Equal(1, arr.GetArrayLength());
    }

    [Fact]
    public void Parses_sse_framed_response()
    {
        // The plugin's MCP endpoint may return either SSE (`event:` + `data:`
        // lines) or bare JSON depending on Accept header negotiation.
        string body = "event: message\r\ndata: {\"jsonrpc\":\"2.0\",\"id\":\"abc\",\"result\":{\"contents\":[]}}\r\n\r\n";

        JsonElement result = JsonRpcOverHttp.ExtractResult(body, slotId: "slot-1", operation: "resources/read");

        Assert.True(result.TryGetProperty("contents", out _));
    }

    [Fact]
    public void Throws_on_error_response_with_message_including_slot_and_operation()
    {
        string body = """{"jsonrpc":"2.0","id":"abc","error":{"code":-32601,"message":"Method not found"}}""";

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            JsonRpcOverHttp.ExtractResult(body, slotId: "octopus", operation: "tool 'frob'"));

        Assert.Contains("octopus", ex.Message);
        Assert.Contains("tool 'frob'", ex.Message);
    }

    [Fact]
    public void Throws_when_neither_result_nor_error_present()
    {
        string body = """{"jsonrpc":"2.0","id":"abc"}""";

        Assert.Throws<InvalidOperationException>(() =>
            JsonRpcOverHttp.ExtractResult(body, slotId: "x", operation: "y"));
    }

    [Fact]
    public void Throws_when_sse_response_has_no_data_payload()
    {
        string body = "event: ping\r\n\r\n";

        Assert.Throws<InvalidOperationException>(() =>
            JsonRpcOverHttp.ExtractResult(body, slotId: "x", operation: "y"));
    }
}
