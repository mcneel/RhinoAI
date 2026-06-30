using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

// Forwards MCP tool calls from this router to the specified child Rhino's HTTP MCP endpoint.
// Plugin runs its MCP server with `Stateless = true`, so no initialize handshake is required —
// each tool call is a self-contained JSON-RPC POST.
//
// Results are wrapped in a ReturnResult envelope (payload/error/autoSpawnedSlot). The lone
// exception is `get_viewport_image`: its binary content block is passed through verbatim
// to avoid a base64 round-trip.
public class ProxyDispatcher(
    RhinoManager manager,
    IHttpClientFactory httpFactory,
    RhinoCrashReportFinder crashFinder,
    ILogger<ProxyDispatcher> log)
{
    private const string ViewportImageToolName = "get_viewport_image";

    public async Task<string> CallToolAsync(
        string? slotId,
        string toolName,
        JsonNode args,
        CancellationToken ct = default,
        string? defaultVersionOverride = null)
    {
        // `defaultVersionOverride` is set by codegen for tools that need a
        // specific Rhino when no slot is passed (GH2_* tools pin "WIP" so they
        // don't try to run on Rhino 8). It has no effect when `slotId` is set.
        ChildRhino? child = null;
        SlotInfo? autoSpawnedSlot = null;
        try
        {
            // Null slot → use (or lazily create) the default Rhino. Lets agents
            // call `run_python(script=...)` etc. without a prior spawn_slot. Note
            // this can throw the same spawn-time exceptions SpawnSlotTool handles
            // (timeout, file-not-found, etc.); the outer catch translates them.
            if (slotId is null)
            {
                (ChildRhino resolved, bool wasNewlySpawned) =
                    await manager.GetOrCreateDefaultAsync(defaultVersionOverride, ct).ConfigureAwait(false);
                child = resolved;
                if (wasNewlySpawned)
                {
                    autoSpawnedSlot = new SlotInfo(
                        SlotId: resolved.SlotId,
                        Version: resolved.Version,
                        Reason: $"Auto-spawned Rhino {resolved.Version} to serve '{toolName}' (no `slot` argument was passed and no matching Rhino was already running).");
                }
            }
            else
            {
                child = manager.Get(slotId) ?? throw new SlotNotFoundException(slotId);
                // Explicit slot whose Rhino version doesn't match what this tool
                // needs (GH2_* tools pin "WIP"). Short-circuit before forwarding —
                // the plugin would otherwise return a generic "unknown tool" MCP
                // error and the agent wouldn't know the cause was a version mismatch.
                if (defaultVersionOverride is not null && !VersionMatch.IsCompatible(child.Version, defaultVersionOverride))
                {
                    return WrapError(
                        new ErrorInfo(
                            Code: "wrong_rhino_version",
                            Message: $"Tool '{toolName}' only works on Rhino {defaultVersionOverride} but slot '{slotId}' is running Rhino {child.Version}. " +
                                $"Omit the `slot` argument to auto-spawn Rhino {defaultVersionOverride}, or call spawn_slot with version=\"{defaultVersionOverride}\"."),
                        autoSpawnedSlot);
                }
            }

            // Make this the session's active slot so the next slot-less call
            // sticks to it. Past the version gate, so an incompatible explicit
            // slot can't become active.
            manager.SetActiveSlot(child.SlotId);

            string requestId = Guid.NewGuid().ToString("N");
            JsonRpcRequest rpc = new(
                Jsonrpc: "2.0",
                Id: requestId,
                Method: "tools/call",
                Params: new JsonRpcRequestParams(Name: toolName, Arguments: args));

            string json = JsonSerializer.Serialize(rpc, RouterJsonContext.Default.JsonRpcRequest);
            log.LogDebug("Proxying tool '{Tool}' to slot '{Slot}' at {Endpoint}: {Body}",
                toolName, slotId, child.Endpoint, json);

            HttpClient http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(5); // some tool calls can run long Python scripts

            using StringContent content = new(json, Encoding.UTF8, "application/json");
            using HttpRequestMessage request = new(HttpMethod.Post, child.Endpoint + "/")
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
            catch (HttpRequestException ex) when (SpawnDiagnostics.IsConnectionFailure(ex))
            {
                // A clean shutdown leaves a departure tombstone; a crash doesn't.
                // Check that first so a user closing the doc/Rhino mid-call isn't
                // reported as a crash.
                if (child.Pid is { } pid && child.Port is { } port && manager.TryConsumeDeparture(pid, port))
                {
                    log.LogInformation("Rhino slot '{Slot}' (pid {Pid}) was closed during tool call '{Tool}'",
                        child.SlotId, child.Pid, toolName);
                    return WrapClosed(child, toolName, callerSlotArg: slotId, autoSpawnedSlot);
                }

                // Connection-level failure — Rhino likely crashed. Confirm via
                // pid + port probe so we don't shout "crashed" on a transient blip.
                if (manager.TryReapDead(child.SlotId))
                {
                    log.LogWarning(ex, "Rhino slot '{Slot}' (pid {Pid}) crashed during tool call '{Tool}'",
                        child.SlotId, child.Pid, toolName);
                    return WrapCrash(child, toolName, callerSlotArg: slotId, autoSpawnedSlot);
                }
                throw;
            }

            using (response)
            {
                string responseBody = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Child Rhino at {child.Endpoint} returned HTTP {(int)response.StatusCode}: {responseBody}");
                }

                JsonElement resultElement = ExtractMcpResult(responseBody, child.SlotId, toolName);

                // Binary content block — pass through verbatim, bypassing the envelope.
                if (toolName == ViewportImageToolName)
                {
                    return resultElement.GetRawText();
                }

                JsonNode? payload = ExtractPayload(resultElement);
                return new ReturnResult(payload, Error: null, AutoSpawnedSlot: autoSpawnedSlot).AsJson;
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
            return WrapError(DiagnoseFailure(ex, toolName), autoSpawnedSlot);
        }
    }

    private static string WrapError(ErrorInfo error, SlotInfo? autoSpawnedSlot) =>
        new ReturnResult(Payload: null, Error: error, AutoSpawnedSlot: autoSpawnedSlot).AsJson;

    private static string WrapClosed(ChildRhino child, string toolName, string? callerSlotArg, SlotInfo? autoSpawnedSlot)
    {
        // Auto-spawn path can just retry; an explicit slot needs a fresh spawn_slot.
        string nextAction = callerSlotArg is null
            ? "Retry this call to auto-spawn another Rhino."
            : "Call spawn_slot to start a new one.";
        string message =
            $"Rhino slot '{child.SlotId}' (Rhino {child.Version}) was closed (its document or the Rhino window was closed) " +
            $"during '{toolName}'. The slot has been pruned. " + nextAction;
        return WrapError(new ErrorInfo(Code: "rhino_closed", Message: message), autoSpawnedSlot);
    }

    private string WrapCrash(ChildRhino child, string toolName, string? callerSlotArg, SlotInfo? autoSpawnedSlot)
    {
        RhinoCrashReport? report = crashFinder.TryFind(child.Pid);
        // Auto-spawn path can just retry; an explicit slot needs a fresh spawn_slot.
        string nextAction = callerSlotArg is null
            ? "Retry this call to auto-spawn another Rhino."
            : "Call spawn_slot to start a new one.";
        string message =
            $"Rhino slot '{child.SlotId}' (pid {child.Pid}, Rhino {child.Version}) is no longer responding — likely crashed mid-call to '{toolName}'. " +
            "The stale slot has been pruned. " + nextAction;
        return WrapError(
            new ErrorInfo(Code: "rhino_crashed", Message: message, CrashReportPath: report?.Path),
            autoSpawnedSlot);
    }

    // Shared spawn-pipeline shapes route through SpawnDiagnostics (the default
    // auto-spawn can fail before we ever talk to a child); this caller appends its
    // tool-call next-action suffix. The arms below are tool-call-specific.
    private ErrorInfo DiagnoseFailure(Exception ex, string toolName)
    {
        if (SpawnDiagnostics.TryClassify(ex, crashFinder, out SpawnDiagnostics.SpawnDiagnosis diag))
        {
            string suffix = diag.Code switch
            {
                "rhino_not_installed" => $" Tool call '{toolName}' aborted because the default Rhino couldn't be auto-spawned.",
                "existing_rhino_unreachable" => " Retry the call.",
                _ => "",
            };
            return new(diag.Code, diag.BaseMessage + suffix, diag.CrashReportPath);
        }

        return ex switch
        {
            SlotNotFoundException snf => new(
                Code: "slot_not_found",
                Message: $"No slot named '{snf.SlotId}'. Call spawn_slot to create one, or list_slots to see what's running."),

            // Non-connection HttpRequestException (HTTP 5xx from the plugin, etc.)
            // Rhino is alive but the request failed. Surface the message.
            HttpRequestException hre => new(
                Code: "plugin_http_error",
                Message: hre.Message),

            InvalidOperationException ioe => new(
                Code: "tool_call_failed",
                Message: ioe.Message),

            _ => new(
                Code: "unexpected",
                Message: $"{ex.GetType().Name}: {ex.Message}"),
        };
    }

    private sealed class SlotNotFoundException(string slotId) : Exception($"No slot named '{slotId}'")
    {
        public string SlotId { get; } = slotId;
    }

    // Unwraps the MCP `result` element from either a bare JSON-RPC body or an SSE stream.
    private static JsonElement ExtractMcpResult(string responseBody, string slotId, string toolName)
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
                        return ExtractResultFromJsonRpc(jsonPart, slotId, toolName);
                    }
                }
            }
            throw new InvalidOperationException(
                $"No `data:` payload in SSE response from slot '{slotId}' for tool '{toolName}': {responseBody}");
        }

        return ExtractResultFromJsonRpc(responseBody, slotId, toolName);
    }

    private static JsonElement ExtractResultFromJsonRpc(string rpcJson, string slotId, string toolName)
    {
        using JsonDocument doc = JsonDocument.Parse(rpcJson);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("error", out JsonElement err))
        {
            throw new InvalidOperationException(
                $"Slot '{slotId}' tool '{toolName}' returned MCP error: {err.GetRawText()}");
        }

        if (root.TryGetProperty("result", out JsonElement result))
        {
            return result.Clone();
        }

        throw new InvalidOperationException(
            $"Unexpected MCP response from slot '{slotId}' tool '{toolName}': {rpcJson}");
    }

    // Plugin tool returns are wrapped by the MCP SDK in a text content block.
    // Pull content[0].text and parse as JSON so structured payloads ride as nodes;
    // fall back to a string value for plain text returns (e.g. "Done.").
    private static JsonNode? ExtractPayload(JsonElement mcpResult)
    {
        if (mcpResult.ValueKind != JsonValueKind.Object) return null;
        if (!mcpResult.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind != JsonValueKind.Array ||
            content.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement first = content[0];
        if (first.ValueKind != JsonValueKind.Object ||
            !first.TryGetProperty("text", out JsonElement textEl) ||
            textEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string text = textEl.GetString() ?? "";
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }
}
