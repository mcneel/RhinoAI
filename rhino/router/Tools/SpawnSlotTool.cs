using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class SpawnSlotTool(RhinoManager manager, RhinoCrashReportFinder crashFinder)
{
    [McpServerTool(Name = "spawn_slot", Title = "Spawn Rhino Slot", ReadOnly = false, Destructive = false)]
    [Description("Launch a new Rhino instance and return its slot ID. Pass that ID as the `slot` arg on subsequent tool calls to target this Rhino.")]
    public async Task<string> SpawnAsync(
        [Description("Rhino version: '8', '9', 'BETA', or 'WIP' ('9'/'BETA'/'WIP' are interchangeable). Omit to use the router's configured default.")]
        string? version = null,
        CancellationToken ct = default)
    {
        try
        {
            ChildRhino child = await manager.SpawnAsync(version, ct).ConfigureAwait(false);
            JsonNode? payload = JsonSerializer.SerializeToNode(child, RouterJsonContext.Default.ChildRhino);
            return new ReturnResult(payload, Error: null, AutoSpawnedSlot: null).AsJson;
        }
        catch (Exception ex)
        {
            // MCP SDK would otherwise swallow the message into a generic "An error
            // occurred…". Stack traces stay in the router log.
            ErrorInfo error = Diagnose(ex, crashFinder);
            return new ReturnResult(Payload: null, Error: error, AutoSpawnedSlot: null).AsJson;
        }
    }

    [McpServerTool(Name = "close_slot", Title = "Close Rhino Slot", ReadOnly = false, Destructive = true)]
    [Description("Close a Rhino slot gracefully. Saves nothing. On success `payload.closed` is true. On failure `error.code` is one of: `slot_not_found` (no slot with that ID is currently running), `cannot_close_adopted` (the slot was a user-started Rhino, the router will not kill it; ask the user to close the Rhino window), `close_failed` (the router tried to close the slot but the operation did not complete; the slot may still be running).")]
    public async Task<string> CloseAsync(
        [Description("Slot ID returned by spawn_slot, or an animal-name slot adopted from a user-started Rhino")]
        string slot,
        CancellationToken ct = default)
    {
        if (!manager.Has(slot))
        {
            ErrorInfo notFound = new(
                Code: "slot_not_found",
                Message: $"No slot named ({slot}). Call list_slots to see what is running.");
            return new ReturnResult(Payload: null, Error: notFound, AutoSpawnedSlot: null).AsJson;
        }

        try
        {
            bool closed = await manager.CloseAsync(slot, ct).ConfigureAwait(false);
            if (!closed)
            {
                // A bare false means the close attempt itself failed (e.g. the Mac
                // sibling control-channel call threw and was logged). Surface a typed
                // code so the agent has something to branch on, not closed:false with
                // error:null which matches neither arm of the contract.
                ErrorInfo error = new(
                    Code: "close_failed",
                    Message: $"Slot '{slot}' could not be closed; it may still be running. " +
                        "Retry close_slot, or ask the user to close the Rhino window.");
                return new ReturnResult(Payload: null, Error: error, AutoSpawnedSlot: null).AsJson;
            }
            JsonObject payload = new() { ["closed"] = true };
            return new ReturnResult(payload, Error: null, AutoSpawnedSlot: null).AsJson;
        }
        catch (AdoptedSlotCloseException ex)
        {
            ErrorInfo error = new(
                Code: "cannot_close_adopted",
                Message: ex.Message + " Ask the user to close the Rhino window themselves.");
            return new ReturnResult(Payload: null, Error: error, AutoSpawnedSlot: null).AsJson;
        }
    }

    [McpServerTool(Name = "list_slots", Title = "List Rhino Slots", ReadOnly = true, Destructive = false)]
    [Description("List all currently-running Rhino slots managed by this router. Slots whose Rhino has crashed are pruned before returning. User-started Rhinos that have advertised themselves since the last call are adopted into the list. The list lives in `payload` (an array of slot objects).")]
    public string List()
    {
        // Adopt anything the plugin has announced since the last call, then probe
        // each slot before reporting; a crashed Rhino otherwise looks alive until
        // something tries to call into it.
        manager.ScanAnnouncements();
        manager.ReapAllDead();
        IReadOnlyCollection<ChildRhino> slots = manager.List();
        JsonNode? payload = JsonSerializer.SerializeToNode(
            slots, RouterJsonContext.Default.IReadOnlyCollectionChildRhino);
        return new ReturnResult(payload, Error: null, AutoSpawnedSlot: null).AsJson;
    }

    // Shared spawn-pipeline shapes route through SpawnDiagnostics; this caller
    // appends its spawn_slot next-action suffix. The arms below are spawn-specific.
    internal static ErrorInfo Diagnose(Exception ex, RhinoCrashReportFinder crashFinder)
    {
        if (SpawnDiagnostics.TryClassify(ex, crashFinder, out SpawnDiagnostics.SpawnDiagnosis diag))
        {
            string suffix = diag.Code switch
            {
                "rhino_not_installed" => " Pass an installed version as the `version` arg, or install the requested Rhino.",
                "existing_rhino_unreachable" => " Call spawn_slot again to launch a fresh Rhino.",
                _ => "",
            };
            return new(diag.Code, diag.BaseMessage + suffix, diag.CrashReportPath);
        }

        return ex switch
        {
            OperationCanceledException => new(
                Code: "cancelled",
                Message: "Spawn was cancelled before Rhino finished starting."),

            InvalidOperationException ioe => new(
                Code: "spawn_failed",
                Message: ioe.Message),

            // Non-connection HttpRequestException (a non-2xx from the Mac control
            // endpoint during the listener fan-out, RhinoControlClient.SpawnListenerAsync).
            // SpawnDiagnostics only owns the connection-level shape; this status-code
            // case is spawn_slot-specific. The Rhino we tried to reuse answered but
            // refused, so treat it as unreachable: surface a crash report if one
            // exists and steer the agent to spawn a fresh Rhino.
            HttpRequestException hre => new(
                Code: "existing_rhino_unreachable",
                Message: hre.Message + " Call spawn_slot again to launch a fresh Rhino.",
                CrashReportPath: crashFinder.TryFindMostRecent()?.Path),

            _ => new(
                Code: "unexpected",
                Message: $"{ex.GetType().Name}: {ex.Message}"),
        };
    }
}
