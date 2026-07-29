using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RhMcp.Server;

namespace RhMcp;

internal sealed class McpServer : IDisposable
{
    private WebApplication? App { get; set; }
    private CancellationTokenSource Cts { get; } = new CancellationTokenSource();

    public bool HasStarted => App is not null;

    public int Port { get; private set; }

    public bool Start(RhinoDoc doc, int port)
    {
        if (HasStarted)
            return true;
        Port = port;
        try
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new RhinoLoggerProvider());
#if DEBUG
            builder.Logging.SetMinimumLevel(LogLevel.Information);
#else
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
#endif
            builder.Services.Configure<KestrelServerOptions>(o => o.ListenLocalhost(port));

            builder.Services.AddSingleton(doc);

            App = builder.Build();
            App.MapMcp("/");
            App.MapMcp("/agent", filtered: true);

            _ = App.RunAsync(Cts.Token);

            RhinoApp.WriteLine($"[Rhino MCP] MCP server currently running on http://localhost:{port}/ (in-Rhino agents use /agent)");
            return true;
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Failed to start: {DescribeException(ex)}");
            App = null;
            return false;
        }
    }

    private static string DescribeException(Exception ex)
    {
        var parts = new List<string>();
        for (var cur = ex; cur is not null; cur = cur.InnerException)
            parts.Add($"{cur.GetType().FullName}: {cur.Message}");
        return string.Join(" --> ", parts);
    }

    public void Stop()
    {
        try
        { Cts?.Cancel(); }
        catch { }
        try
        { App?.StopAsync(); }
        catch { }
        App = null;
    }

    public void Dispose() => Stop();
}
