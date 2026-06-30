using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RhMcp.Integration.Tests.Harness;

// One-stop harness for ngentic-style fixtures that drive the rhino MCP:
// spawns the freshly-built router in an isolated TMPDIR, hands back the tool
// list as AITool instances (ready to drop into ChatOptions.Tools), and exposes
// the handful of slot-management calls a fixture wants to make outside the
// agent's trajectory (spawn/close/list_objects).
//
// The agent invokes tools via the FunctionInvokingChatClient → the AITool list
// here; the fixture invokes the same tools out-of-band via direct MCP calls.
// Both share the same router process, so they see the same slot store.
internal sealed class RhinoMcpFixture : IAsyncDisposable
{
    private readonly RhinoMcpRouter _router;
    private readonly IReadOnlyList<AITool> _tools;

    private RhinoMcpFixture(RhinoMcpRouter router, IReadOnlyList<AITool> tools)
    {
        _router = router;
        _tools = tools;
    }

    public IReadOnlyList<AITool> Tools => _tools;

    public static async Task<RhinoMcpFixture> CreateAsync(CancellationToken ct = default)
    {
        RhinoMcpRouter router = await RhinoMcpRouter.LaunchIsolatedAsync(ct).ConfigureAwait(false);
        try
        {
            // ListToolsAsync paginates internally; we get the full set in one call.
            IList<McpClientTool> tools = await router.Client.ListToolsAsync(cancellationToken: ct)
                .ConfigureAwait(false);
            // McpClientTool : AIFunction : AITool, so the upcast is trivial.
            return new RhinoMcpFixture(router, tools.Cast<AITool>().ToList());
        }
        catch
        {
            await router.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<string> SpawnSlotAsync(string? version = null, CancellationToken ct = default)
    {
        Dictionary<string, object?>? args = version is null
            ? null
            : new Dictionary<string, object?> { ["version"] = version };
        ReturnResult result = await _router.CallToolAsync("spawn_slot", args, ct).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            throw new InvalidOperationException(
                $"spawn_slot returned error '{error.Code}': {error.Message}\nFull payload: {result.Payload}");
        }
        if (result.Payload is not { } payload
            || !payload.TryGetProperty("slotId", out JsonElement slotIdEl)
            || slotIdEl.GetString() is not string slotId)
        {
            throw new InvalidOperationException($"spawn_slot returned no slotId: {result.Payload}");
        }
        return slotId;
    }

    public async Task CloseSlotAsync(string slotId, CancellationToken ct = default)
    {
        string text = await _router.CallToolTextAsync(
            "close_slot",
            new Dictionary<string, object?> { ["slot"] = slotId },
            ct).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(text);
        // close_slot returns { closed: bool, error?: string, message?: string }.
        // We don't throw on closed=false because teardown is best-effort — a
        // slot the agent already closed shows up here as slot_not_found.
    }

    public async Task<JsonDocument> ListObjectsAsync(string slotId, CancellationToken ct = default)
    {
        string text = await _router.CallToolTextAsync(
            "list_objects",
            new Dictionary<string, object?> { ["slot"] = slotId },
            ct).ConfigureAwait(false);
        return JsonDocument.Parse(text);
    }

    public ValueTask DisposeAsync() => _router.DisposeAsync();
}
