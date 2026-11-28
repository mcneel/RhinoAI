using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Generic;

namespace Rhino.AI;

/// <summary>
/// A HTTP MCP that drives a server
/// </summary>
public sealed class HttpMcp(string name, Uri url, IDictionary<string, string> headers) : Mcp(name)
{

    private const string SessionHeader = "Mcp-Session-Id";
    private const string ProtocolHeader = "MCP-Protocol-Version";
    private const string EventStream = "text/event-stream";

    public HttpMcp(string name, Uri url) : this(name, url, new Dictionary<string, string>())
    {
    }

    public Uri Url { get; } = url;

    public IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>(headers);

    // Harness.UseToolAsync bounds each call by McpTimeouts.ToolCall, and HttpClient's 100 second default would cut long Rhino tools short.
    private HttpClient Http { get; } = new() { Timeout = Timeout.InfiniteTimeSpan };

    private int LastId { get; set; }

    private string? SessionId { get; set; }

    private string? NegotiatedVersion { get; set; }

    private bool Initialized { get; set; }

    public override async Task<bool> InitAsync(CancellationToken token)
    {
        if (Initialized) return true;

        SessionId = null;
        NegotiatedVersion = null;

        using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        startup.CancelAfter(McpTimeouts.Startup);

        IReadOnlyList<McpClient.ListedTool> tools;
        try
        {
            tools = await McpClient.ConnectAsync(RequestAsync, NotifyAsync, startup.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"MCP '{Name}' at {Url} did not start within {McpTimeouts.Startup}.");
        }

        foreach (McpClient.ListedTool tool in tools)
            PrivateTools[tool.Name] = new HttpTool(this, tool.Name, tool.Description, tool.ReadOnly, tool.Destructive, tool.Args);

        Initialized = true;
        return true;
    }

    public async Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!Initialized) await InitAsync(token).ConfigureAwait(false);

        if (!PrivateTools.TryGetValue(toolName, out ITool? tool))
            return ToolReturn.UnknownTool(Name, toolName);

        return await McpClient.CallToolAsync(RequestAsync, tool, args, token).ConfigureAwait(false);
    }

    public override bool RegisterTool(ITool tool) => false;

    private async Task<JsonNode?> RequestAsync(string method, JsonObject? parameters, CancellationToken token)
    {
        JsonObject message = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ++LastId,
            ["method"] = method,
        };
        if (parameters is not null)
            message["params"] = parameters;

        string wanted = LastId.ToString(CultureInfo.InvariantCulture);
        using HttpResponseMessage response = await PostAsync(message, method, token).ConfigureAwait(false);

        JsonNode reply = string.Equals(response.Content.Headers.ContentType?.MediaType, EventStream, StringComparison.OrdinalIgnoreCase)
            ? await ReadEventStreamAsync(response, wanted, method, token).ConfigureAwait(false)
            : await ReadJsonAsync(response, wanted, method, token).ConfigureAwait(false);

        if (reply["error"] is JsonNode error)
            throw new InvalidOperationException($"MCP '{Name}' {method} failed: {error.ToJsonString()}");

        if (method == "initialize")
        {
            SessionId = response.Headers.TryGetValues(SessionHeader, out IEnumerable<string>? sessions) ? sessions.FirstOrDefault() : null;
            NegotiatedVersion = reply["result"]?["protocolVersion"]?.ToString();
        }

        return reply["result"];
    }

    private async Task NotifyAsync(string method, CancellationToken token)
    {
        using HttpResponseMessage response = await PostAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }, method, token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> PostAsync(JsonObject message, string method, CancellationToken token)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Url)
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(EventStream));
        foreach (KeyValuePair<string, string> header in Headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (SessionId is string session)
            request.Headers.TryAddWithoutValidation(SessionHeader, session);
        if (NegotiatedVersion is string version)
            request.Headers.TryAddWithoutValidation(ProtocolHeader, version);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"MCP '{Name}' could not reach {Url} during {method}: {ex.Message}", ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            throw new InvalidOperationException($"MCP '{Name}' {method} failed with HTTP {(int)response.StatusCode}: {body}");
        }
    }

    private async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response, string wanted, string method, CancellationToken token)
    {
        string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        return Reply(body, wanted) ?? throw new InvalidOperationException($"MCP '{Name}' did not answer {method} with its JSON-RPC reply: {body}");
    }

    private async Task<JsonNode> ReadEventStreamAsync(HttpResponseMessage response, string wanted, string method, CancellationToken token)
    {
        using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using StreamReader reader = new(stream, Encoding.UTF8);

        StringBuilder data = new();
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is string line)
        {
            if (line.Length > 0)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (data.Length > 0) data.Append('\n');
                    data.Append(line.Length > 5 && line[5] == ' ' ? line[6..] : line[5..]);
                }

                continue;
            }

            if (Reply(data.ToString(), wanted) is JsonNode reply)
                return reply;

            data.Clear();
        }

        throw new InvalidOperationException($"MCP '{Name}' ended its event stream without answering {method}.");
    }

    private static JsonNode? Reply(string json, string wanted)
        => McpClient.ParseJson(json) is JsonObject message && message["id"]?.ToJsonString() == wanted ? message : null;

    public override void Dispose()
    {
        Http.Dispose();
        PrivateTools.Clear();
    }

    private sealed record HttpTool(HttpMcp Host, string Name, string Description, bool ReadOnly, bool Destructive, ToolParameter[] Args) : ITool
    {
        public Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
            => Host.RunToolAsync(Name, args, token);
    }

}
