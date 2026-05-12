namespace RhMcp.Router;

// Forwards MCP tool calls from this router to the specified child Rhino's HTTP MCP endpoint.
// Each child Rhino exposes the standard rhino-mcp plugin tools at http://localhost:<port>/.
// The router-side tool wrappers add a `slot` argument, look the child up, and delegate here.
public class ProxyDispatcher(RhinoManager manager, IHttpClientFactory httpFactory)
{
    public async Task<string> CallToolAsync(string slotId, string toolName, object args, CancellationToken ct = default)
    {
        var child = manager.Get(slotId)
            ?? throw new InvalidOperationException($"No slot named '{slotId}'. Call spawn_slot first.");

        // TODO: open an MCP client connection to child.Endpoint, invoke toolName with args,
        // serialise the result back as a string. The MCP SDK has a client-side API for this —
        // need to verify the exact namespace in v0.4.0-preview.6.
        //
        // For v1 a thin manual JSON-RPC POST to /mcp would also work; cleaner to use the SDK client.

        throw new NotImplementedException("Proxy call not yet wired up.");
    }
}
