using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rhino;

namespace RhMcp;

internal sealed class McpServer : IDisposable
{
    private WebApplication? _app;
    private CancellationTokenSource? _cts;

    public bool HasStarted => _app is not null;

    public int Port { get; private set; }

    public bool Start(RhinoDoc doc, int port)
    {
        if (HasStarted) return true;
        Port = port;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.Configure<KestrelServerOptions>(o => o.ListenLocalhost(port));

            builder.Services.AddSingleton(doc);

            builder.Services
                .AddMcpServer(o =>
                {
                    o.ServerInfo = new() { Name = "rhino-mcp", Version = "0.1.0" };
                })
                .WithHttpTransport(o => o.Stateless = true)
                .WithToolsFromAssembly(typeof(McpServer).Assembly)
                .WithResourcesFromAssembly(typeof(McpServer).Assembly)
                .WithPromptsFromAssembly(typeof(McpServer).Assembly);

            _app = builder.Build();
            _app.MapMcp();

            _cts = new CancellationTokenSource();
            _ = _app.RunAsync(_cts.Token);

            RhinoApp.WriteLine($"[Rhino MCP] MCP server currently running on http://localhost:{port}/");
            return true;
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Failed to start: {ex.Message}");
            _app = null;
            return false;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _app?.StopAsync().GetAwaiter().GetResult(); } catch { }
        _app = null;
    }

    public void Dispose() => Stop();
}
