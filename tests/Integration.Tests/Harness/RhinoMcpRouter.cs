using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RhinoAI.Integration.Tests.Harness;

// Spawns the rhino-mcp-router binary as a child process and connects an MCP
// stdio client to it. Each instance gets its own TMPDIR so the router's
// state.db / listener-announcement dir are isolated from the user's regularly
// running router. Disposal kills the child process via the transport's
// shutdown path.
public sealed class RhinoMcpRouter : IAsyncDisposable
{
    private readonly string _isolatedTempDir;

    public McpClient Client { get; }

    public HashSet<string> OpenedSlots { get; } = new();

    // Mirrors RouterPaths.ListenersDir on the router side. Tests can drop
    // *.json announcement files here to simulate a user-started Rhino.
    public string ListenersDir => Path.Combine(_isolatedTempDir, "rhino-mcp", "listeners");

    private RhinoMcpRouter(string isolatedTempDir, McpClient client)
    {
        _isolatedTempDir = isolatedTempDir;
        Client = client;
    }

    public static async Task<RhinoMcpRouter> LaunchIsolatedAsync(CancellationToken ct = default)
    {
        string isolatedTempDir = RhinoRouterPaths.CreateIsolatedTempDir();
        StdioClientTransportOptions options = new()
        {
            Name = "rhino-mcp-router (test)",
            Command = RhinoRouterPaths.ResolveBinary(),
            EnvironmentVariables = RhinoRouterPaths.IsolatedEnv(isolatedTempDir),
            ShutdownTimeout = TimeSpan.FromMilliseconds(250),
        };
        StdioClientTransport transport = new(options);

        try
        {
            McpClient client = await McpClient.CreateAsync(transport, cancellationToken: ct);
            return new RhinoMcpRouter(isolatedTempDir, client);
        }
        catch
        {
            RhinoRouterPaths.TryDeleteDirectory(isolatedTempDir);
            throw;
        }
    }

    public async Task<string> CallToolTextAsync(
        string toolName,
        Dictionary<string, object?>? arguments = null,
        CancellationToken ct = default)
    {
        CallToolResult result = await Client.CallToolAsync(toolName, arguments, cancellationToken: ct);
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));
    }

    // Typed view of the router's ReturnResult envelope. Bypass: get_viewport_image
    // returns binary content blocks, not the envelope — use CallToolTextAsync for it.
    public async Task<ReturnResult> CallToolAsync(
        string toolName,
        Dictionary<string, object?>? arguments = null,
        CancellationToken ct = default)
    {
        string json = await CallToolTextAsync(toolName, arguments, ct);
        return JsonSerializer.Deserialize<ReturnResult>(json)
            ?? throw new InvalidOperationException(
                $"Tool '{toolName}' returned a null ReturnResult envelope: {json}");
    }

    public async ValueTask DisposeAsync()
    {
        // Close every slot the router is tracking before tearing down the
        // transport. On macOS the app is single-instance: leaving a Rhino
        // alive between tests means the next `open -a` just foregrounds it,
        // the new port never binds, and the spawn waits the full
        // startup timeout before failing. Cooperative close keeps tests
        // independent and fast.
        try
        {
            string listJson = await CallToolTextAsync("list_slots");
            using JsonDocument doc = JsonDocument.Parse(listJson);
            if (doc.RootElement.TryGetProperty("payload", out JsonElement payload)
                && payload.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement slot in payload.EnumerateArray())
                {
                    if (slot.TryGetProperty("slotId", out JsonElement slotIdEl)
                        && slotIdEl.GetString() is string slotId)
                    {
                        _ = await CallToolTextAsync("close_slot", new() { { "slot", slotId } });
                    }
                }
            }
        }
        catch { /* best effort */ }

        try
        {
            await Client.DisposeAsync();
        }
        catch { /* best effort */ }
        RhinoRouterPaths.TryDeleteDirectory(_isolatedTempDir);
    }
}

