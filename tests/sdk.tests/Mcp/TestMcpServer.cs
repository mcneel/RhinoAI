using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace sdk.tests;

public sealed class TestMcpServer : IAsyncDisposable
{

    private WebApplication App { get; }

    public Uri Url { get; }

    private TestMcpServer(WebApplication app, Uri url)
    {
        App = app;
        Url = url;
    }

    public static async Task<TestMcpServer> StartAsync(CancellationToken token)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddMcpServer().WithHttpTransport().WithTools<TestTools>();

        WebApplication app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync(token).ConfigureAwait(false);

        Uri url = new(new Uri(app.Urls.First()), "/mcp");
        return new TestMcpServer(app, url);
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync().ConfigureAwait(false);
        await App.DisposeAsync().ConfigureAwait(false);
    }

}

[McpServerToolType]
public sealed class TestTools
{

    [McpServerTool(Name = "ping")]
    public static string Ping() => "pong";

    [McpServerTool(Name = "repeat")]
    public static string Repeat(string text, int count, string separator = " ")
        => string.Join(separator, Enumerable.Repeat(text, count));

}
