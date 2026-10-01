using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

namespace Rhino.AI.Mcps;

// TODO : Public this!
internal static class MemoryMcpManager
{

    private record struct UrlKey(string key);

    private static HttpListener Listener { get; } = new();
    private static bool Started { get; set; } = false;
    private static string? ListenerUrl { get; set; } = null;

    // TODO : Replace GUIDs with colours so it's more fun and easier to debug?
    private static ConcurrentDictionary<string, MemoryMcp> RegisteredMcps { get; } = [];

    // TODO : Not concurrent
    private static ConditionalWeakTable<MemoryMcp, string> RegisteredGuids { get; } = new();

    public static McpLease RegisterMemoryMcp(MemoryMcp mcp)
    {
        // TODO : Handle better
        if (!Start()) throw new Exception("Could not create lease");

        string guidKey = RegisteredGuids.GetValue(mcp, _ => Guid.NewGuid().ToString().ToLowerInvariant());
        RegisteredMcps[guidKey] = mcp;

        Uri uri = new($"{ListenerUrl}/{guidKey}/");
        McpLease lease = new(uri);
        return lease;
    }

    private static readonly object StartLock = new();

    private static bool Start()
    {
        lock (StartLock)
        {
            if (Started) return true;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Started = true;
                    System.Net.Sockets.TcpListener tcp = new(IPAddress.Loopback, 0);
                    tcp.Start();
                    int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
                    tcp.Stop();
                    ListenerUrl = $"http://localhost:{port}";
                    Listener.Prefixes.Add($"{ListenerUrl}/");
                    Listener.Start();

                    _ = AcceptLoopAsync();

                    // Success!
                    return true;
                }
                catch
                {
                    Started = false;
                    Stop();
                }
            }
        }

        return false;
    }

    private static async Task AcceptLoopAsync()
    {
        while (Listener.IsListening)
        {
            try
            {
                HttpListenerContext context = await Listener.GetContextAsync();
                _ = Task.Run(() => ServeAsync(context, CancellationToken.None));
            }
            catch (HttpListenerException)
            {
                // TODO : Report
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private static async Task ServeAsync(HttpListenerContext context, CancellationToken token)
    {
        try
        {
            if (context.Request.Url is null) return;
            string[] segments = context.Request.Url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 1) return;
            if (RegisteredMcps.TryGetValue(segments[0], out MemoryMcp? memMcp) && memMcp is not null)
            {
                McpHttpRequest request = await McpHttpRequest.FromRequestAsync(context.Request, token);
                McpSession session = new(memMcp, new PermissionSet());
                McpResponse response = await McpProtocol.HandleAsync(request, session, token);

                context.Response.StatusCode = (int)response.Status;
                if (response.Body is not null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(response.Body.ToJsonString());
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, token);
                }
            }
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static void Stop()
    {
        Listener.Stop();
        Listener.Prefixes.Clear();
    }

    private static void DeRegisterMemoryMcp(Uri uri)
    {

    }

    public sealed class McpLease : IDisposable
    {

        public Uri Uri { get; }

        internal McpLease(Uri uri)
        {
            Uri = uri;
        }

        public void Dispose()
        {
            DeRegisterMemoryMcp(Uri);
        }

    }

}
