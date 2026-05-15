using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace RhMcp.Router.Tools;

[McpServerToolType]
public class SpawnSlotTool(RhinoManager manager, RhinoCrashReportFinder crashFinder)
{
    [McpServerTool(Name = "spawn_slot")]
    [Description("Launch a new Rhino instance and return its slot ID. Pass that ID as the `slot` arg on subsequent tool calls to target this Rhino.")]
    public async Task<string> SpawnAsync(
        [Description("Rhino version: '8', '9', or 'WIP'. Omit to use the router's configured default.")]
        string? version = null,
        CancellationToken ct = default)
    {
        try
        {
            var child = await manager.SpawnAsync(version, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(child, RouterJsonContext.Default.ChildRhino);
        }
        catch (Exception ex)
        {
            // The MCP SDK swallows raw exception messages into a generic "An error
            // occurred…", so we translate the exception into a stable, agent-readable
            // payload (kebab-case error code + actionable message + crash report when
            // we can find one). Stack traces are deliberately omitted — they belong
            // in the router log, not in the agent's tool result.
            var payload = Diagnose(ex);
            return JsonSerializer.Serialize(payload, RouterJsonContext.Default.SpawnErrorPayload);
        }
    }

    [McpServerTool(Name = "close_slot")]
    [Description("Close a Rhino slot gracefully. Saves nothing. Returns { closed: bool, error?: string, message?: string }. `error=\"cannot_close_adopted\"` means the slot was a user-started Rhino — the router will not kill it; ask the user to close the Rhino window.")]
    public async Task<string> CloseAsync(
        [Description("Slot ID returned by spawn_slot, or an animal-name slot adopted from a user-started Rhino")]
        string slot,
        CancellationToken ct = default)
    {
        try
        {
            var ok = await manager.CloseAsync(slot, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(new CloseSlotResult(ok), RouterJsonContext.Default.CloseSlotResult);
        }
        catch (AdoptedSlotCloseException ex)
        {
            var payload = new CloseSlotResult(
                Closed: false,
                Error: "cannot_close_adopted",
                Message: ex.Message + " Ask the user to close the Rhino window themselves.");
            return JsonSerializer.Serialize(payload, RouterJsonContext.Default.CloseSlotResult);
        }
    }

    [McpServerTool(Name = "list_slots")]
    [Description("List all currently-running Rhino slots managed by this router. Slots whose Rhino has crashed are pruned before returning. User-started Rhinos that have advertised themselves since the last call are adopted into the list.")]
    public IReadOnlyCollection<ChildRhino> List()
    {
        // Adopt anything the plugin has announced since the last call, then probe
        // each slot before reporting; a crashed Rhino otherwise looks alive until
        // something tries to call into it.
        manager.ScanAnnouncements();
        manager.ReapAllDead();
        return manager.List();
    }

    // Map a raw exception from the spawn pipeline to an agent-readable diagnosis.
    // Every branch ends in a message that tells the agent what to do next (retry,
    // change args, check Rhino UI, give up). The `existing_rhino_unreachable`
    // branch is enriched with the latest crash report when one exists.
    private SpawnErrorPayload Diagnose(Exception ex) => ex switch
    {
        FileNotFoundException fnf => new(
            "rhino_not_installed",
            fnf.Message + " Pass an installed version as the `version` arg, or install the requested Rhino."),

        TimeoutException te => new(
            "startup_timeout",
            te.Message + " The Rhino window may be showing a license, EULA, or update dialog — check it. " +
            "If the rh-mcp plugin isn't loaded, install it and retry."),

        PlatformNotSupportedException pne => new(
            "unsupported_platform",
            pne.Message),

        OperationCanceledException => new(
            "cancelled",
            "Spawn was cancelled before Rhino finished starting."),

        // HttpRequestException from the spawn chain only originates inside
        // RhinoControlClient when fanning out a new listener on Mac. That means
        // we tried to reuse an existing Rhino and its control endpoint didn't
        // answer — the Rhino likely crashed between probe and call.
        HttpRequestException hre => new(
            "existing_rhino_unreachable",
            "Tried to add a listener to a previously-spawned Rhino but its control endpoint didn't respond " +
            $"({hre.Message}). The Rhino likely crashed between the liveness probe and this call. " +
            "The stale slot has been pruned — call spawn_slot again to launch a fresh Rhino.",
            crashFinder.TryFindMostRecent()),

        InvalidOperationException ioe => new(
            "spawn_failed",
            ioe.Message),

        _ => new(
            "unexpected",
            $"{ex.GetType().Name}: {ex.Message}"),
    };
}
