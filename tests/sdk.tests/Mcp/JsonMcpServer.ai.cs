using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace sdk.tests;

// Answers like Rhino's own McpDispatcher: plain JSON replies, 204 for notifications, no session.
public sealed class JsonMcpServer : IAsyncDisposable
{

    private WebApplication App { get; }

    public Uri Url { get; }

    private JsonMcpServer(WebApplication app, Uri url)
    {
        App = app;
        Url = url;
    }

    public static async Task<JsonMcpServer> StartAsync(CancellationToken token)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        WebApplication app = builder.Build();
        app.MapPost("/mcp", HandleAsync);
        await app.StartAsync(token).ConfigureAwait(false);

        Uri url = new(new Uri(app.Urls.First()), "/mcp");
        return new JsonMcpServer(app, url);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        JsonNode? request = await JsonNode.ParseAsync(context.Request.Body).ConfigureAwait(false);
        if (request?["id"] is not JsonNode id)
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        JsonObject result = request["method"]?.ToString() switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = "2025-06-18",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "json-test", ["version"] = "1.0.0" },
            },
            "tools/list" => new JsonObject
            {
                ["tools"] = new JsonArray(
                    Tool("echo", new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } }, "text"),
                    Tool("authorization", new JsonObject())),
            },
            "tools/call" => Text(request["params"]?["name"]?.ToString() switch
            {
                "echo" => request["params"]?["arguments"]?["text"]?.ToString() ?? "",
                "authorization" => context.Request.Headers.Authorization.ToString(),
                string other => $"unknown tool {other}",
                null => "missing tool name",
            }),
            string other => throw new InvalidOperationException($"Unexpected method {other}."),
            null => throw new InvalidOperationException("Missing method."),
        };

        JsonObject reply = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(reply.ToJsonString()).ConfigureAwait(false);
    }

    private static JsonObject Tool(string name, JsonObject properties, params string[] required) => new()
    {
        ["name"] = name,
        ["description"] = name,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray([.. required.Select(r => JsonValue.Create(r))]),
        },
    };

    private static JsonObject Text(string text) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
    };

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync().ConfigureAwait(false);
        await App.DisposeAsync().ConfigureAwait(false);
    }

}
