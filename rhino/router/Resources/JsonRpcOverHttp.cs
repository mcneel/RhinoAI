using System.Text.Json;

namespace RhMcp.Router.Resources;

// Shared JSON-RPC-over-HTTP response parser for talking to child Rhino MCP
// endpoints. Handles both bare JSON and SSE-framed responses. Used by
// ProxyDispatcher for tool calls and by ResourceProxy for resource list/read.
//
// `slotId` and `operation` are used only to build error messages; pass
// something self-describing like `$"tool '{toolName}'"` or
// `$"resources/read '{uri}'"`.
internal static class JsonRpcOverHttp
{
    // Unwraps the MCP `result` element from either a bare JSON-RPC body or an
    // SSE stream. Throws InvalidOperationException on malformed or error-shaped
    // responses; the thrown message is suitable for logging.
    public static JsonElement ExtractResult(string responseBody, string slotId, string operation)
    {
        string trimmed = responseBody.TrimStart();

        if (trimmed.StartsWith("event:") || trimmed.StartsWith("data:"))
        {
            // Walk SSE lines, find the first `data:` payload, parse that.
            foreach (string line in responseBody.Split('\n'))
            {
                string lineTrimmed = line.TrimEnd('\r');
                if (lineTrimmed.StartsWith("data:"))
                {
                    string jsonPart = lineTrimmed["data:".Length..].TrimStart();
                    if (!string.IsNullOrEmpty(jsonPart))
                    {
                        return ExtractResultFromJsonRpc(jsonPart, slotId, operation);
                    }
                }
            }
            throw new InvalidOperationException(
                $"No `data:` payload in SSE response from slot '{slotId}' for {operation}: {responseBody}");
        }

        return ExtractResultFromJsonRpc(responseBody, slotId, operation);
    }

    private static JsonElement ExtractResultFromJsonRpc(string rpcJson, string slotId, string operation)
    {
        using JsonDocument doc = JsonDocument.Parse(rpcJson);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("error", out JsonElement err))
        {
            throw new InvalidOperationException(
                $"Slot '{slotId}' {operation} returned MCP error: {err.GetRawText()}");
        }

        if (root.TryGetProperty("result", out JsonElement result))
        {
            return result.Clone();
        }

        throw new InvalidOperationException(
            $"Unexpected MCP response from slot '{slotId}' {operation}: {rpcJson}");
    }
}
