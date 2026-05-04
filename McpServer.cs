using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Rhino;

namespace RhMcp;

internal sealed class McpServer : IDisposable
{
    private HttpListener Listener { get; } = new HttpListener();
    public bool HasStarted => Listener.IsListening;
    private CancellationTokenSource? _cts;

    // Discover all IMcpTool implementations in this assembly at startup.
    private static IReadOnlyDictionary<string, IMcpTool> Tools { get; } =
        typeof(IMcpTool).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                     && typeof(IMcpTool).IsAssignableFrom(t))
            .Select(t => (IMcpTool)Activator.CreateInstance(t)!)
            .ToDictionary(t => t.Name);

    public bool Start()
    {
        if (HasStarted) return false;
        try
        {
            Listener.Prefixes.Add($"http://localhost:{RhMcpHost.Port}/");
            Listener.Start();
            _cts = new CancellationTokenSource();
            _ = ListenAsync(_cts.Token);
            RhinoApp.WriteLine($"[Rhino MCP] Listening on http://localhost:{RhMcpHost.Port}/ ({Tools.Count} tools)");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { Listener?.Stop(); } catch { }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await Listener!.GetContextAsync().WaitAsync(ct);
                _ = HandleAsync(ctx);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                RhinoApp.WriteLine($"[Rhino MCP] Error: {ex.Message}");
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";

        if (ctx.Request.HttpMethod == "OPTIONS")
        {
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            ctx.Response.StatusCode = 204;
            ctx.Response.Close();
            return;
        }

        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var req = JsonNode.Parse(body)!.AsObject();

            var id = req["id"];
            var method = req["method"]?.GetValue<string>() ?? "";
            var @params = req["params"]?.AsObject();

            if (method.StartsWith("notifications/"))
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

            object? result = method switch
            {
                "initialize" => new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "rhino-mcp", version = "0.1.0" }
                },
                "tools/list" => new
                {
                    tools = Tools.Values.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        inputSchema = t.InputSchema
                    }).ToArray()
                },
                "tools/call" => DispatchTool(@params),
                _ => null
            };

            var envelope = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["result"] = JsonSerializer.SerializeToNode(result)
            };

            await WriteJsonAsync(ctx, envelope.ToJsonString(), 200);
        }
        catch (Exception ex)
        {
            var error = $"{{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{{\"code\":-32603,\"message\":{JsonSerializer.Serialize(ex.Message)}}}}}";
            await WriteJsonAsync(ctx, error, 200);
        }
    }

    private static object DispatchTool(JsonObject? @params)
    {
        var name = @params?["name"]?.GetValue<string>() ?? "";
        if (!Tools.TryGetValue(name, out var tool))
            return new { isError = true, content = new[] { new { type = "text", text = $"Unknown tool: {name}" } } };

        try
        {
            return tool.Execute(@params?["arguments"]?.AsObject());
        }
        catch (Exception ex)
        {
            return new { isError = true, content = new[] { new { type = "text", text = ex.Message } } };
        }
    }

    private static async Task WriteJsonAsync(HttpListenerContext ctx, string json, int status)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = status;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose() => Stop();
}
