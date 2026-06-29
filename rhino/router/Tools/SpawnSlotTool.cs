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
            ErrorInfo error = Diagnose(ex);
            return new ReturnResult(Payload: null, Error: error, AutoSpawnedSlot: null).AsJson;
        }
    }

    [McpServerTool(Name = "close_slot", Title = "Close Rhino Slot", ReadOnly = false, Destructive = true)]
    [Description("Close a Rhino slot gracefully. Saves nothing. On success `payload.closed` is true. On failure `error.code` is one of: `slot_not_found` (no slot with that ID is currently running), `cannot_close_adopted` (the slot was a user-started Rhino — the router will not kill it; ask the user to close the Rhino window).")]
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
            JsonObject payload = new() { ["closed"] = closed };
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

    // Map spawn-pipeline exception → ErrorInfo. Each message ends with the next action.
    private ErrorInfo Diagnose(Exception ex) => ex switch
    {
        FileNotFoundException fnf => new(
            Code: "rhino_not_installed",
            Message: fnf.Message + " Pass an installed version as the `version` arg, or install the requested Rhino."),

        TimeoutException te => new(
            Code: "startup_timeout",
            Message: te.Message + " The Rhino window may be showing a license, EULA, or update dialog — check it. " +
            "If the rh-mcp plugin isn't loaded, install it and retry."),

        PlatformNotSupportedException pne => new(
            Code: "unsupported_platform",
            Message: pne.Message),

        OperationCanceledException => new(
            Code: "cancelled",
            Message: "Spawn was cancelled before Rhino finished starting."),

        // HttpRequestException from the spawn chain only originates inside
        // RhinoControlClient when fanning out a new listener on Mac. That means
        // we tried to reuse an existing Rhino and its control endpoint didn't
        // answer — the Rhino likely crashed between probe and call.
        HttpRequestException hre => new(
            Code: "existing_rhino_unreachable",
            Message: "Tried to add a listener to a previously-spawned Rhino but its control endpoint didn't respond " +
                $"({hre.Message}). The Rhino likely crashed between the liveness probe and this call. " +
                "The stale slot has been pruned — call spawn_slot again to launch a fresh Rhino.",
            CrashReportPath: crashFinder.TryFindMostRecent()?.Path),

        InvalidOperationException ioe => new(
            Code: "spawn_failed",
            Message: ioe.Message),

        _ => new(
            Code: "unexpected",
            Message: $"{ex.GetType().Name}: {ex.Message}"),
    };
}
