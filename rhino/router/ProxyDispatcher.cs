using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

// Forwards MCP tool calls from this router to the specified child Rhino's HTTP MCP endpoint.
// Plugin runs its MCP server with `Stateless = true`, so no initialize handshake is required —
// each tool call is a self-contained JSON-RPC POST.
public class ProxyDispatcher(
    RhinoManager manager,
    IHttpClientFactory httpFactory,
    RhinoCrashReportFinder crashFinder,
    ILogger<ProxyDispatcher> log)
{
    public async Task<string> CallToolAsync(
        string? slotId,
        string toolName,
        JsonNode args,
        CancellationToken ct = default,
        string? defaultVersionOverride = null)
    {
        // Every exit path returns a string the MCP SDK forwards to the agent
        // verbatim. The SDK swallows exception messages into a generic "An error
        // occurred invoking '<tool>'", so on failure we MUST return a structured
        // payload, not throw. The catch at the bottom of this method is the
        // safety net for anything we didn't anticipate.
        //
        // `defaultVersionOverride` is set by codegen for tools that need a
        // specific Rhino when no slot is passed (GH2_* tools pin "WIP" so they
        // don't try to run on Rhino 8). It has no effect when `slotId` is set.
        ChildRhino? child = null;
        try
        {
            // Null slot → use (or lazily create) the default Rhino. Lets agents
            // call `run_python(script=...)` etc. without a prior spawn_slot. Note
            // this can throw the same spawn-time exceptions SpawnSlotTool handles
            // (timeout, file-not-found, etc.); the outer catch translates them.
            if (slotId is null)
            {
                child = await manager.GetOrCreateDefaultAsync(defaultVersionOverride, ct).ConfigureAwait(false);
            }
            else
            {
                child = manager.Get(slotId) ?? throw new SlotNotFoundException(slotId);
                // Explicit slot whose Rhino version doesn't match what this tool
                // needs (GH2_* tools pin "WIP"). Short-circuit before forwarding —
                // the plugin would otherwise return a generic "unknown tool" MCP
                // error and the agent wouldn't know the cause was a version mismatch.
                if (defaultVersionOverride is not null && !IsVersionCompatible(child.Version, defaultVersionOverride))
                {
                    return SerializePayload(new SpawnErrorPayload(
                        "wrong_rhino_version",
                        $"Tool '{toolName}' only works on Rhino {defaultVersionOverride} but slot '{slotId}' is running Rhino {child.Version}. " +
                        $"Omit the `slot` argument to auto-spawn Rhino {defaultVersionOverride}, or call spawn_slot with version=\"{defaultVersionOverride}\"."));
                }
            }

            var requestId = Guid.NewGuid().ToString("N");
            var rpc = new JsonRpcRequest(
                Jsonrpc: "2.0",
                Id: requestId,
                Method: "tools/call",
                Params: new JsonRpcRequestParams(Name: toolName, Arguments: args));

            var json = JsonSerializer.Serialize(rpc, RouterJsonContext.Default.JsonRpcRequest);
            log.LogDebug("Proxying tool '{Tool}' to slot '{Slot}' at {Endpoint}: {Body}",
                toolName, slotId, child.Endpoint, json);

            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(5); // some tool calls can run long Python scripts

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, child.Endpoint + "/")
            {
                Content = content
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            }
            catch (HttpRequestException ex) when (IsConnectionFailure(ex))
            {
                // Connection-level failure — Rhino likely crashed. Confirm via
                // pid + port probe so we don't shout "crashed" on a transient blip.
                if (manager.TryReapDead(child.SlotId))
                {
                    log.LogWarning(ex, "Rhino slot '{Slot}' (pid {Pid}) crashed during tool call '{Tool}'",
                        child.SlotId, child.Pid, toolName);
                    return SerializePayload(BuildCrashPayload(child, toolName));
                }
                throw;
            }

            using (response)
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Child Rhino at {child.Endpoint} returned HTTP {(int)response.StatusCode}: {responseBody}");
                }

                return ExtractResult(responseBody, child.SlotId, toolName);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-driven cancellation — propagate so the SDK reports it as cancelled
            // rather than a tool-level error. (Hosted timeouts are different and surface
            // as OperationCanceledException too, but those originate from our own
            // CancellationToken sources and the SDK handles them the same way.)
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Tool call '{Tool}' (slot '{Slot}') failed", toolName, slotId ?? "(default)");
            return SerializePayload(DiagnoseFailure(ex, child, toolName));
        }
    }

    private static string SerializePayload(SpawnErrorPayload p) =>
        JsonSerializer.Serialize(p, RouterJsonContext.Default.SpawnErrorPayload);

    private SpawnErrorPayload BuildCrashPayload(ChildRhino child, string toolName) =>
        new(
            Error: "rhino_crashed",
            Message:
                $"Rhino slot '{child.SlotId}' (pid {child.Pid}, Rhino {child.Version}) is no longer responding — likely crashed mid-call to '{toolName}'. " +
                "The stale slot has been pruned. " +
                (child.SlotId.StartsWith(RhinoManager.DefaultSlotPrefix)
                    ? "Retry this call to auto-spawn a fresh default Rhino."
                    : "Call spawn_slot to start a new one."),
            CrashReport: crashFinder.TryFind(child.Pid));

    // Translates anything that escaped the inner crash-detection branch into the
    // same SpawnErrorPayload shape SpawnSlotTool emits. Codes are kebab-case so
    // an agent can branch on them. Messages always end with what the agent
    // should do next.
    private SpawnErrorPayload DiagnoseFailure(Exception ex, ChildRhino? child, string toolName) => ex switch
    {
        SlotNotFoundException snf => new(
            "slot_not_found",
            $"No slot named '{snf.SlotId}'. Call spawn_slot to create one, or list_slots to see what's running."),

        // The default-Rhino auto-spawn can fail before we ever talk to a child.
        // Same exception shapes SpawnSlotTool handles; keep the messages in sync.
        FileNotFoundException fnf => new(
            "rhino_not_installed",
            fnf.Message + " Tool call '" + toolName + "' aborted because the default Rhino couldn't be auto-spawned."),

        TimeoutException te => new(
            "startup_timeout",
            te.Message + " The Rhino window may be showing a license, EULA, or update dialog — check it. " +
            "If the rh-mcp plugin isn't loaded, install it and retry."),

        PlatformNotSupportedException pne => new(
            "unsupported_platform",
            pne.Message),

        // HttpRequestException with a connection error here means an *existing*
        // Rhino we were trying to reuse stopped responding (typically during the
        // Mac _router_spawn_listener call inside GetOrCreateDefaultAsync).
        HttpRequestException hre when IsConnectionFailure(hre) => new(
            "existing_rhino_unreachable",
            $"Tried to reach an existing Rhino to handle tool call '{toolName}' but it didn't respond " +
            $"({hre.Message}). The Rhino likely crashed. Stale slot pruned — retry the call.",
            crashFinder.TryFindMostRecent()),

        // Non-connection HttpRequestException (HTTP 5xx from the plugin, etc.)
        // — Rhino is alive but the request failed. Surface the message.
        HttpRequestException hre => new(
            "plugin_http_error",
            hre.Message),

        InvalidOperationException ioe => new(
            "tool_call_failed",
            ioe.Message),

        _ => new(
            "unexpected",
            $"{ex.GetType().Name}: {ex.Message}"),
    };

    private sealed class SlotNotFoundException(string slotId) : Exception($"No slot named '{slotId}'")
    {
        public string SlotId { get; } = slotId;
    }

    private static bool IsVersionCompatible(string actual, string required)
    {
        if (actual == required) return true;
        return (actual, required) switch
        {
            ("9", "WIP") => true,
            ("WIP", "9") => true,
            _ => false,
        };
    }

    // Detect transport-level failures (DNS, refused, reset, timeout) — these mean
    // Rhino's listener isn't there to respond, not that Rhino responded with an
    // error. .NET 8 exposes HttpRequestError; older causes (SocketException) are
    // also unwrapped here for belt-and-braces.
    private static bool IsConnectionFailure(HttpRequestException ex)
    {
        if (ex.HttpRequestError == HttpRequestError.ConnectionError) return true;
        if (ex.HttpRequestError == HttpRequestError.SecureConnectionError) return true;
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is System.Net.Sockets.SocketException) return true;
            if (inner is IOException) return true;
        }
        return false;
    }

    private static string ExtractResult(string responseBody, string slotId, string toolName)
    {
        // The plugin's Stateless HTTP transport returns either:
        //   - A bare JSON-RPC object: {"jsonrpc":"2.0","id":"...","result":{...}}
        //   - An SSE-style stream with one or more `data: <json>` lines (when the SDK chooses streaming).
        // We handle both.
        var payload = responseBody.TrimStart();

        if (payload.StartsWith("event:") || payload.StartsWith("data:"))
        {
            // Walk SSE lines, find the first `data:` payload, parse that.
            foreach (var line in responseBody.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.StartsWith("data:"))
                {
                    var jsonPart = trimmed["data:".Length..].TrimStart();
                    if (!string.IsNullOrEmpty(jsonPart))
                    {
                        return ExtractFromJsonRpc(jsonPart, slotId, toolName);
                    }
                }
            }
            throw new InvalidOperationException(
                $"No `data:` payload in SSE response from slot '{slotId}' for tool '{toolName}': {responseBody}");
        }

        return ExtractFromJsonRpc(responseBody, slotId, toolName);
    }

    private static string ExtractFromJsonRpc(string rpcJson, string slotId, string toolName)
    {
        using var doc = JsonDocument.Parse(rpcJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
        {
            throw new InvalidOperationException(
                $"Slot '{slotId}' tool '{toolName}' returned MCP error: {err.GetRawText()}");
        }

        if (root.TryGetProperty("result", out var result))
        {
            return result.GetRawText();
        }

        throw new InvalidOperationException(
            $"Unexpected MCP response from slot '{slotId}' tool '{toolName}': {rpcJson}");
    }
}
