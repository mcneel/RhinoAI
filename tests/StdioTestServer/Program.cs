using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using StdioTestServer;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    DisableDefaults = true,
});

// Stdout is the JSON-RPC channel.
builder.Logging.ClearProviders();
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<TestTools>();

await builder.Build().RunAsync();
