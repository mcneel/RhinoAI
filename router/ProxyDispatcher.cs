using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

// Forwards MCP tool calls from this router to the specified child Rhino's HTTP MCP endpoint.
// Plugin runs its MCP server with `Stateless = true`, so no initialize handshake is required —
// each tool call is a self-contained JSON-RPC POST.
public class ProxyDispatcher(RhinoManager manager, IHttpClientFactory httpFactory, ILogger<ProxyDispatcher> log)
{
    public async Task<string> CallToolAsync(
        string slotId,
        string toolName,
        object args,
        CancellationToken ct = default)
    {
        var child = manager.Get(slotId)
            ?? throw new InvalidOperationException(
                $"No slot named '{slotId}'. Call spawn_slot to create one, or list_slots to see what's running.");

        var requestId = Guid.NewGuid().ToString("N");
        var payload = new
        {
            jsonrpc = "2.0",
            id = requestId,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = args
            }
        };

        var json = JsonSerializer.Serialize(payload);
        log.LogDebug("Proxying tool '{Tool}' to slot '{Slot}' at {Endpoint}: {Body}",
            toolName, slotId, child.Endpoint, json);

        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5); // some tool calls can run long Python scripts

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, child.Endpoint + "/")
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Child Rhino at {child.Endpoint} returned HTTP {(int)response.StatusCode}: {responseBody}");
        }

        return ExtractResult(responseBody, slotId, toolName);
    }

    private static string ExtractResult(string responseBody, string slotId, string toolName)
    {
        // The plugin's Stateless HTTP transport returns either:
        //   - A bare JSON-RPC object: {"jsonrpc":"2.0","id":"...","result":{...}}
        //   - An SSE-style stream with one or more `data: <json>` lines (when the SDK chooses streaming).
        // We handle both.
        var payload = responseBody.TrimStart();

        if (payload.StartsWith("event:") || payload.StartsWith("data:"))
        {
            // Walk SSE lines, find the first `data:` payload, parse that.
            foreach (var line in responseBody.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.StartsWith("data:"))
                {
                    var jsonPart = trimmed["data:".Length..].TrimStart();
                    if (!string.IsNullOrEmpty(jsonPart))
                    {
                        return ExtractFromJsonRpc(jsonPart, slotId, toolName);
                    }
                }
            }
            throw new InvalidOperationException(
                $"No `data:` payload in SSE response from slot '{slotId}' for tool '{toolName}': {responseBody}");
        }

        return ExtractFromJsonRpc(responseBody, slotId, toolName);
    }

    private static string ExtractFromJsonRpc(string rpcJson, string slotId, string toolName)
    {
        using var doc = JsonDocument.Parse(rpcJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
        {
            throw new InvalidOperationException(
                $"Slot '{slotId}' tool '{toolName}' returned MCP error: {err.GetRawText()}");
        }

        if (root.TryGetProperty("result", out var result))
        {
            return result.GetRawText();
        }

        throw new InvalidOperationException(
            $"Unexpected MCP response from slot '{slotId}' tool '{toolName}': {rpcJson}");
    }
}
