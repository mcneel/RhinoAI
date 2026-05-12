using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RhMcp.Router;

var config = RouterConfig.FromArgs(args);

var builder = Host.CreateApplicationBuilder(args);

// Stdio MCP servers must not log to stdout — that's the JSON-RPC channel.
// Route all logging to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<RhinoLocator>();
builder.Services.AddSingleton<RhinoManager>();
builder.Services.AddSingleton<ProxyDispatcher>();
builder.Services.AddHttpClient();

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "rhino-mcp-router", Version = "0.1.0" };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

// Clean up child Rhinos when Claude Code closes the stdio connection.
host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() =>
{
    host.Services.GetRequiredService<RhinoManager>().CloseAll();
});

await host.RunAsync();
