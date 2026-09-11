using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rhino.AI.Server;

namespace Rhino.AI;

internal sealed class McpServer : IDisposable
{
    // The running WebApplication, held as object so that no member of this class is
    // typed by Microsoft.AspNetCore.
    //
    // Rhino's host loads Microsoft.NETCore.App and Microsoft.WindowsDesktop.App only,
    // so on an install where ASP.NET Core is neither bundled nor resolvable, every
    // method whose body or signature names one of its types fails to JIT. That failure
    // is raised in the CALLER, which is why a FileLoadException for Microsoft.AspNetCore
    // came out of `HasStarted` - a property Start's own try/catch never gets to guard.
    // Keeping those types inside RunApp/StopApp below, both [MethodImpl(NoInlining)] and
    // both called from inside a try, confines the failure to a place that can catch it:
    // the server reports that it did not start instead of taking Rhino down.
    private object? App { get; set; }
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
            App = RunApp(doc, port, Cts.Token);

            RhinoApp.WriteLine($"[RhinoAI] MCP server currently running on http://localhost:{port}/ (in-Rhino agents use /agent)");
            return true;
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[RhinoAI] Failed to start: {DescribeException(ex)}");
            App = null;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object RunApp(RhinoDoc doc, int port, CancellationToken token)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            // Prevent unnecesssary file watchers
            Args = ["--hostBuilder:reloadConfigOnChange=false"],
            ContentRootPath = Path.GetDirectoryName(typeof(McpServer).Assembly.Location),
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new RhinoLoggerProvider());
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.Configure<KestrelServerOptions>(o => o.ListenLocalhost(port));

        builder.Services.AddSingleton(doc);

        WebApplication app = builder.Build();
        app.MapMcp("/");
        app.MapMcp("/agent", filtered: true);

        _ = app.RunAsync(token);

        return app;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StopApp(object app) => ((WebApplication)app).StopAsync();

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
        {
            if (App is object app)
                StopApp(app);
        }
        catch { }
        App = null;
    }

    public void Dispose() => Stop();
}
