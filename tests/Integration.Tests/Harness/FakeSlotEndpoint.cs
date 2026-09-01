using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RhMcp.Integration.Tests.Harness;

/// <summary>
/// A stand-in for a running Rhino's MCP HTTP endpoint.
/// </summary>
/// <remarks>
/// <para>
/// AdoptedSlotTests fakes a slot with a bare TcpListener, which is enough for the router's
/// IsPortListening probe. That is not enough here: the router now asks a slot's
/// <c>_router_list_contributed_tools</c> to discover contributed tools, so the fake has to
/// answer that <c>tools/call</c> over the plugin's stateless JSON-RPC-over-POST shape.
/// </para>
/// <para>
/// The advertised set is mutable so a test can register a tool mid-run, which is the case that
/// matters: a plug-in registering ten seconds after Rhino started is exactly what the router
/// used to miss entirely. <c>tools/list</c> deliberately answers a superset — compiled-looking
/// names such as <c>GH2_preview</c> and the control channel alongside the advertised tools —
/// so a router that regresses to inferring "contributed" by subtracting its compiled names
/// from <c>tools/list</c> will surface <c>GH2_preview</c> and fail the tests.
/// </para>
/// </remarks>
public sealed class FakeSlotEndpoint : IDisposable
{
    /// <summary>
    /// What a real plugin's <c>tools/list</c> carries besides contributed tools: its own
    /// compiled tools (including GH2_* ones a differently-targeted router never compiled
    /// proxies for) and the router's private control channel.
    /// </summary>
    private static readonly object[] BaselineCompiledTools =
    [
        new
        {
            name = "run_python",
            description = "A compiled plugin tool.",
            inputSchema = new { type = "object", properties = new { } },
        },
        new
        {
            name = "GH2_preview",
            description = "A compiled plugin tool this router's build excluded on purpose.",
            inputSchema = new { type = "object", properties = new { } },
        },
        new
        {
            name = "_router_spawn_listener",
            description = "The router's private control channel.",
            inputSchema = new { type = "object", properties = new { } },
        },
    ];

    private readonly HttpListener _listener;
    private readonly List<object> _tools = [];
    private readonly object _toolsGate = new();

    public int Port { get; }

    /// <summary>
    /// Every tools/call this endpoint received, in order — except the router's own
    /// <c>_router_list_contributed_tools</c> control calls, which precede every listing and
    /// would otherwise bury the call a test actually made.
    /// </summary>
    public ConcurrentQueue<RecordedCall> Calls { get; } = new();

    public sealed record RecordedCall(string Name, JsonElement Arguments);

    private FakeSlotEndpoint(HttpListener listener, int port)
    {
        _listener = listener;
        Port = port;
        _ = Task.Run(ServeAsync);
    }

    public static FakeSlotEndpoint Start()
    {
        // Bind-and-release to pick a free port, then claim it with HttpListener. Loopback
        // prefixes do not need a URL ACL, so this works unelevated.
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        HttpListener listener = new();
        // "localhost", not "127.0.0.1": ChildRhino.Endpoint is http://localhost:<port>, and real
        // Rhino binds it with Kestrel's ListenLocalhost, which covers 127.0.0.1 and ::1 both. An
        // IPv4-only prefix is refused whenever the resolver hands out ::1 first. The localhost
        // prefix is also the one Windows permits without a URL ACL.
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        return new FakeSlotEndpoint(listener, port);
    }

    /// <summary>
    /// Adds a tool to what this endpoint reports as contributed. Safe to call while the router
    /// is running; the next pull sees it.
    /// </summary>
    public void Advertise(string name, string description = "A contributed tool.")
    {
        lock (_toolsGate)
        {
            _tools.Add(new
            {
                name,
                description,
                inputSchema = new { type = "object", properties = new { } },
            });
        }
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) { return; }   // disposed

            try { await RespondAsync(context); }
            catch (Exception) { /* the router treats a failed slot as contributing nothing */ }
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
        string body = await reader.ReadToEndAsync();

        using JsonDocument request = JsonDocument.Parse(body);
        string method = request.RootElement.GetProperty("method").GetString() ?? "";

        object result;
        if (method == "tools/list")
        {
            lock (_toolsGate)
            {
                result = new { tools = BaselineCompiledTools.Concat(_tools).ToArray() };
            }
        }
        else if (method == "tools/call")
        {
            JsonElement parameters = request.RootElement.GetProperty("params");
            string name = parameters.GetProperty("name").GetString() ?? "";

            if (name == "_router_list_contributed_tools")
            {
                // The authoritative answer: only the advertised (i.e. runtime-registered)
                // tools, as a JSON array riding in content[0].text -- the same wrapping the
                // plugin's tool host applies to a string return. Not recorded in Calls.
                string toolsJson;
                lock (_toolsGate) { toolsJson = JsonSerializer.Serialize(_tools.ToArray()); }
                result = new { content = new[] { new { type = "text", text = toolsJson } }, isError = false };
            }
            else
            {
                JsonElement arguments = parameters.TryGetProperty("arguments", out JsonElement a)
                    ? a.Clone()
                    : default;

                Calls.Enqueue(new RecordedCall(name, arguments));
                result = new { content = new[] { new { type = "text", text = "\"ok\"" } }, isError = false };
            }
        }
        else
        {
            result = new { };
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = "1", result });
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = payload.Length;
        await context.Response.OutputStream.WriteAsync(payload);
        context.Response.Close();
    }

    public void Dispose()
    {
        try { _listener.Stop(); } catch { /* best effort */ }
        try { _listener.Close(); } catch { /* best effort */ }
    }
}
