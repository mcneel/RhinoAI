using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

// Client for the plugin's router-private control tools (e.g. _router_spawn_listener).
// Talks to the same MCP HTTP endpoint a slot uses, but for internal tools that
// the agent-facing source generator deliberately doesn't proxy.
//
// Mac uses this to fan out additional listeners inside an already-running Rhino
// process; Windows doesn't need it (each slot is its own process).
public class RhinoControlClient(IHttpClientFactory httpFactory, ILogger<RhinoControlClient> log)
{
    public async Task<int> SpawnListenerAsync(string endpoint, CancellationToken ct)
    {
        var argsJson = JsonSerializer.SerializeToNode(new SpawnListenerArgs(), RouterJsonContext.Default.SpawnListenerArgs)!;
        var resultJson = await PostAsync(endpoint, "_router_spawn_listener", argsJson, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(resultJson);
        return doc.RootElement.GetProperty("port").GetInt32();
    }

    public Task CloseListenerAsync(string endpoint, int port, CancellationToken ct)
    {
        var argsJson = JsonSerializer.SerializeToNode(new CloseListenerArgs(port), RouterJsonContext.Default.CloseListenerArgs)!;
        return PostAsync(endpoint, "_router_close_listener", argsJson, ct);
    }

    private async Task<string> PostAsync(string endpoint, string toolName, JsonNode args, CancellationToken ct)
    {
        var payload = new JsonRpcRequest(
            Jsonrpc: "2.0",
            Id: Guid.NewGuid().ToString("N"),
            Method: "tools/call",
            Params: new JsonRpcRequestParams(Name: toolName, Arguments: args));

        var json = JsonSerializer.Serialize(payload, RouterJsonContext.Default.JsonRpcRequest);
        log.LogDebug("Control: calling {Tool} at {Endpoint}", toolName, endpoint);

        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint + "/") { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Control call {toolName} returned HTTP {(int)resp.StatusCode}: {body}");

        return ExtractToolText(body, toolName);
    }

    // Plugin tools return a JSON string; the MCP SDK wraps it as
    // result.content[0].text. Response transport may be either bare JSON-RPC
    // or SSE — same shape as ProxyDispatcher handles.
    private static string ExtractToolText(string responseBody, string toolName)
    {
        var payload = responseBody.TrimStart();
        string rpcJson;
        if (payload.StartsWith("event:") || payload.StartsWith("data:"))
        {
            var dataLine = responseBody
                .Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .FirstOrDefault(l => l.StartsWith("data:"))
                ?? throw new InvalidOperationException($"No `data:` line in SSE response for {toolName}: {responseBody}");
            rpcJson = dataLine["data:".Length..].TrimStart();
        }
        else
        {
            rpcJson = responseBody;
        }

        using var doc = JsonDocument.Parse(rpcJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException($"Control tool {toolName} returned MCP error: {err.GetRawText()}");

        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException($"Control tool {toolName} returned unexpected payload: {rpcJson}");

        if (result.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Array
            && content.GetArrayLength() > 0
            && content[0].TryGetProperty("text", out var text))
        {
            return text.GetString() ?? "";
        }

        // Fallback: return the result blob as-is.
        return result.GetRawText();
    }
}
