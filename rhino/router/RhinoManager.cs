using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

// Spawns, tracks, and tears down Rhino "slots".
//
// State lives in SlotStore (SQLite) so concurrent router processes — one per
// Claude Code session — can't race on port allocation or duplicate-spawn the
// Mac shared-process lead. The manager itself holds no in-memory registry;
// every read/write goes through the store under BEGIN IMMEDIATE.
//
// Process model differs by OS:
//   Windows: one OS process per slot. Each child gets its own RhinoDoc on its
//            own private port that only the router talks to.
//   macOS:   at most one OS process per Rhino version (Rhino is single-instance
//            per bundle id). The first slot for a version launches the .app;
//            subsequent slots for the same version share that pid and ask the
//            existing listener to spawn another doc + listener via the
//            _router_spawn_listener control tool. The "first slot" is decided
//            inside SlotStore.Reserve so two routers can't both claim leader.
public class RhinoManager(
    RhinoLocator locator,
    RouterConfig config,
    RhinoControlClient control,
    SlotStore store,
    ILogger<RhinoManager> log)
{
    // Slot-id prefix for the auto-spawned default Rhino used by tool calls that
    // don't pass an explicit slot. The version is appended so a GH2 tool that
    // needs WIP gets its own default slot (e.g. "default-WIP") rather than
    // colliding with a non-GH2 tool's "default-8".
    public const string DefaultSlotPrefix = "default-";

    // Children walk forward from the conventional RhinoMCP port (10500) to find a free one.
    // A user who started a Rhino manually via `_RhinoMCP` will already be on 10500, so the
    // router-spawned children land on 10501, 10502, ... without colliding.
    private const int ChildPortBase = 10500;
    private const int SpawnTimeoutSeconds = 60;
    private static readonly TimeSpan StaleLaunchingMaxAge = TimeSpan.FromSeconds(90);

    private readonly int _routerPid = Environment.ProcessId;

    public Task<ChildRhino> SpawnAsync(string? version = null, CancellationToken ct = default)
    {
        var resolved = version ?? config.DefaultVersion;
        // Name allocation lives in the store (cross-router-safe). We use the
        // returned slot_id as-is; no separate AnimalNames.Next() per process.
        store.ReapStaleLaunching(StaleLaunchingMaxAge);
        ReapAllDead();
        var (reservation, slotId) = store.ReserveNewNamed(resolved, _routerPid);
        return DispatchReservationAsync(resolved, slotId, reservation, ct);
    }

    // Lazily return the default slot for `version`, spawning a Rhino if one doesn't already exist.
    // Called by ProxyDispatcher when a tool is invoked without an explicit slot. A null version
    // resolves to the router's configured default. GH2 tool proxies pass "WIP" so they get a
    // separate default slot from non-GH2 tools. If the user started a Rhino manually and ran
    // `_RhinoMCP`, the drop-file scan adopts it and we reuse that session (when its version
    // matches) instead of spawning a parallel one — the user's manual launch is the strongest
    // possible signal that they want this Rhino used.
    public async Task<ChildRhino> GetOrCreateDefaultAsync(string? version = null, CancellationToken ct = default)
    {
        ScanAnnouncements();
        string resolved = version ?? config.DefaultVersion;
        string slotId = DefaultSlotPrefix + resolved;

        // Prefer an adopted user-started Rhino on the matching version over
        // spawning our own — see class comment.
        var adopted = store.ListReady().FirstOrDefault(c => c.Adopted && c.Version == resolved);
        if (adopted is not null) return adopted;

        return await SpawnInternalAsync(resolved, slotId, ct).ConfigureAwait(false);
    }

    private async Task<ChildRhino> SpawnInternalAsync(string version, string slotId, CancellationToken ct)
    {
        // Drop stale placeholders before deciding what to do, so a crashed
        // router's abandoned 'launching' row doesn't make this caller wait 90s.
        store.ReapStaleLaunching(StaleLaunchingMaxAge);
        ReapAllDead();

        var reservation = store.Reserve(slotId, version, _routerPid);
        return await DispatchReservationAsync(version, slotId, reservation, ct).ConfigureAwait(false);
    }

    // Shared post-reservation branch. Both SpawnAsync (auto-named via the name
    // pool) and SpawnInternalAsync (known slot_id, e.g. 'default-WIP') funnel
    // through here so the existing/leader/follower handling lives in one place.
    private async Task<ChildRhino> DispatchReservationAsync(
        string version, string slotId, SlotReservation reservation, CancellationToken ct)
    {
        switch (reservation.Kind)
        {
            case SlotReservation.ReservationKind.Existing:
                return await UseOrAwaitExisting(reservation.ExistingSlot!, ct).ConfigureAwait(false);

            case SlotReservation.ReservationKind.Leader:
                return await LaunchAsLeaderAsync(version, slotId, ct).ConfigureAwait(false);

            case SlotReservation.ReservationKind.Follower:
                return await LaunchAsFollowerAsync(version, slotId, ct).ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"Unknown reservation kind: {reservation.Kind}");
        }
    }

    private async Task<ChildRhino> UseOrAwaitExisting(ChildRhino existing, CancellationToken ct)
    {
        if (existing.Status == SlotStatus.Ready) return existing;

        var ready = store.WaitForReady(existing.SlotId, TimeSpan.FromSeconds(SpawnTimeoutSeconds), ct);
        if (ready is null)
        {
            throw new TimeoutException(
                $"Slot '{existing.SlotId}' was already being launched by another router but never became ready " +
                $"within {SpawnTimeoutSeconds}s.");
        }
        return ready;
    }

    private async Task<ChildRhino> LaunchAsLeaderAsync(string version, string slotId, CancellationToken ct)
    {
        try
        {
            var rhinoExe = locator.ResolveRhinoExe(version);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var port = store.ReservePort(slotId, ChildPortBase, IsPortListening);
                log.LogInformation("Spawning Rhino {Version} as slot '{Slot}' on port {Port} (exe: {Exe})",
                    version, slotId, port, rhinoExe);
                var proc = LaunchWindows(rhinoExe, port);
                if (!WaitForPort(port, TimeSpan.FromSeconds(SpawnTimeoutSeconds)))
                {
                    try { proc.Kill(); } catch { /* best effort */ }
                    throw new TimeoutException(
                        $"Rhino {version} (pid {proc.Id}) did not bind port {port} within {SpawnTimeoutSeconds}s. " +
                        $"Possible causes: plugin missing, plugin failed to init, license dialog, slow disk.");
                }
                store.MarkReady(slotId, port, proc.Id);
                log.LogInformation("Slot '{Slot}' ready: pid {Pid}, port {Port}", slotId, proc.Id, port);
                return store.Get(slotId)!;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var port = store.ReservePort(slotId, ChildPortBase, IsPortListening);
                log.LogInformation("Launching Rhino {Version} as slot '{Slot}' on port {Port} (app: {App})",
                    version, slotId, port, rhinoExe);
                LaunchMac(rhinoExe, port);
                if (!WaitForPort(port, TimeSpan.FromSeconds(SpawnTimeoutSeconds)))
                {
                    throw new TimeoutException(
                        $"Rhino {version} did not bind port {port} within {SpawnTimeoutSeconds}s. " +
                        $"Possible causes: plugin missing, plugin failed to init, license dialog, slow disk.");
                }
                var pid = FindPidListeningOnPort(port);
                if (pid == 0)
                {
                    throw new InvalidOperationException(
                        $"Rhino bound port {port} but lsof could not resolve the pid.");
                }
                store.MarkReady(slotId, port, pid);
                log.LogInformation("Slot '{Slot}' ready: pid {Pid}, port {Port}", slotId, pid, port);
                return store.Get(slotId)!;
            }

            throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
        }
        catch
        {
            // Free the placeholder so the next caller can retry instead of
            // waiting 90s for the stale-launching reaper.
            store.Delete(slotId);
            throw;
        }
    }

    private async Task<ChildRhino> LaunchAsFollowerAsync(string version, string slotId, CancellationToken ct)
    {
        // Mac-only path: another reservation for this version exists. Wait
        // until at least one ready row for this version appears (it'll be the
        // leader we follow), then ask that listener to spawn a sibling.
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // On Windows every slot is its own process, so "follower" makes
                // no sense — promote to leader behavior. SlotStore only returns
                // Follower when peers exist, which on Windows would still mean
                // distinct processes, distinct ports.
                return await LaunchAsLeaderAsync(version, slotId, ct).ConfigureAwait(false);
            }

            var lead = await WaitForLeadAsync(version, slotId, ct).ConfigureAwait(false);
            log.LogInformation("Mac: reusing Rhino {Version} (pid {Pid}) for slot '{Slot}'",
                version, lead.Pid, slotId);

            var newPort = await control.SpawnListenerAsync(lead.Endpoint, ct).ConfigureAwait(false);
            store.MarkReady(slotId, newPort, lead.Pid);
            log.LogInformation("Slot '{Slot}' ready: pid {Pid} (shared), port {Port}", slotId, lead.Pid, newPort);
            return store.Get(slotId)!;
        }
        catch
        {
            store.Delete(slotId);
            throw;
        }
    }

    private async Task<ChildRhino> WaitForLeadAsync(string version, string slotId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(SpawnTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var lead = store.FindReadyLead(version, slotId);
            if (lead is not null) return lead;
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Slot '{slotId}' waited {SpawnTimeoutSeconds}s for a sibling Rhino {version} to become ready but none did.");
    }

    public async Task<bool> CloseAsync(string slotId, CancellationToken ct = default)
    {
        var child = store.Get(slotId);
        if (child is null) return false;

        if (child.Adopted)
        {
            // The router didn't start this Rhino, so it doesn't get to kill the
            // process. Tools layer turns this into a structured error so the
            // agent learns why; here we just refuse.
            throw new AdoptedSlotCloseException(slotId);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Find sibling slots sharing this pid. If any exist, this isn't the last
            // slot in the Rhino process — close just this listener via the control
            // channel and keep Rhino running.
            var sibling = store.FindSiblingByPid(slotId, child.Pid);
            if (sibling is not null)
            {
                log.LogInformation("Closing slot '{Slot}' listener on port {Port} (pid {Pid} shared with '{Sibling}')",
                    slotId, child.Port, child.Pid, sibling.SlotId);
                try
                {
                    await control.CloseListenerAsync(sibling.Endpoint, child.Port, ct).ConfigureAwait(false);
                    store.Delete(slotId);
                    return true;
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Failed to close listener for slot '{Slot}' via control channel.", slotId);
                    return false;
                }
            }
            // Last slot for this Rhino — fall through to process kill below.
        }

        store.Delete(slotId);
        log.LogInformation("Closing slot '{Slot}' (pid {Pid})", slotId, child.Pid);
        try
        {
            Process.GetProcessById(child.Pid).Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* already exited */ }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to kill slot '{Slot}' (pid {Pid})", slotId, child.Pid);
        }
        return true;
    }

    public void CloseAll()
    {
        // Shutdown path: kill each unique pid this router owns once. No control-channel
        // niceties — we're tearing everything down anyway, and multiple slots may share a
        // pid on Mac. Adopted slots are skipped: the user owns those Rhinos. Slots owned
        // by *other* routers are also skipped — they're not ours to kill, and their owners
        // will tear them down on their own shutdown.
        var owned = store.ListAllOwnedBy(_routerPid);
        var killed = new HashSet<int>();
        foreach (var c in owned)
        {
            if (c.Adopted) { store.Delete(c.SlotId); continue; }
            store.Delete(c.SlotId);
            if (c.Pid <= 0) continue;
            if (!killed.Add(c.Pid)) continue;
            try
            {
                Process.GetProcessById(c.Pid).Kill(entireProcessTree: true);
            }
            catch (ArgumentException) { /* already exited */ }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to kill pid {Pid} during CloseAll", c.Pid);
            }
        }
    }

    public IReadOnlyCollection<ChildRhino> List() => store.ListReady();

    public ChildRhino? Get(string slotId)
    {
        var c = store.Get(slotId);
        return c is null || c.Status != SlotStatus.Ready ? null : c;
    }

    // Walk the listener-announcement drop directory and adopt any plugin-side
    // Rhino we don't already know about. The plugin writes one file per
    // listener-bind into <temp>/rhino-mcp/listeners; we treat each file as a
    // one-shot "look at me" doorbell — consume by deleting it whether or not
    // adoption succeeded. Stale files (port no longer listening) are dropped
    // silently; files for a pid+port we already track are deleted as a no-op so
    // the Mac _router_spawn_listener path remains idempotent.
    public void ScanAnnouncements()
    {
        var dir = RouterPaths.ListenersDir;
        if (!Directory.Exists(dir)) return;

        string[] files;
        try { files = Directory.GetFiles(dir, "*.json"); }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to enumerate listener-announcement dir {Dir}", dir);
            return;
        }

        foreach (var file in files)
        {
            try
            {
                Announcement? ann;
                try
                {
                    var json = File.ReadAllText(file);
                    ann = JsonSerializer.Deserialize(json, RouterJsonContext.Default.Announcement);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Bad announcement file {File}; deleting", file);
                    TryDelete(file);
                    continue;
                }

                if (ann is null || ann.Port <= 0 || ann.Pid <= 0)
                {
                    TryDelete(file);
                    continue;
                }

                if (!IsPortListening(ann.Port))
                {
                    log.LogDebug("Announcement for pid {Pid} port {Port} is stale (no listener); discarding",
                        ann.Pid, ann.Port);
                    TryDelete(file);
                    continue;
                }

                // AdoptIfNew handles the duplicate-(pid,port) check, name
                // allocation, and the INSERT atomically. Returns null when the
                // listener is already in the registry (Mac _router_spawn_listener
                // duplicate, or another router already adopted it).
                var slotId = store.AdoptIfNew(ann.Version ?? "?", ann.Port, ann.Pid, _routerPid);
                if (slotId is not null)
                {
                    log.LogInformation("Adopted user-started Rhino {Version} as slot '{Slot}' (pid {Pid}, port {Port})",
                        ann.Version ?? "?", slotId, ann.Pid, ann.Port);
                }
                TryDelete(file);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Unexpected failure processing announcement {File}", file);
                TryDelete(file);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* next scan will retry */ }
    }

    // Cheap liveness probe. Considers a slot alive iff its pid still exists AND
    // the listener is accepting connections. Pid-alive alone isn't enough on Mac
    // (multiple slots share a pid; a single listener can die while the app keeps
    // running), and port-listening alone isn't enough on Windows (a zombie or
    // stuck process could leave the socket bound).
    public bool IsAlive(ChildRhino c)
    {
        if (c.Status != SlotStatus.Ready) return true; // launching rows aren't "dead", just pending
        if (!IsProcessAlive(c.Pid)) return false;
        if (!IsPortListening(c.Port)) return false;
        return true;
    }

    // Probe one slot; if dead, drop it from the registry. Returns true if reaped.
    // Used by ProxyDispatcher when an HTTP call to a slot fails with a connection
    // error, so the next tool call doesn't keep hitting the same dead slot.
    public bool TryReapDead(string slotId)
    {
        var c = store.Get(slotId);
        if (c is null) return false;
        if (IsAlive(c)) return false;
        store.Delete(slotId);
        log.LogWarning("Reaped dead slot '{Slot}' (pid {Pid}, port {Port}, Rhino {Version})",
            c.SlotId, c.Pid, c.Port, c.Version);
        return true;
    }

    // Probe every ready slot, drop the dead ones, return what was reaped. Called
    // by list_slots so a silently-crashed Rhino doesn't appear healthy until the
    // next tool call.
    public IReadOnlyCollection<ChildRhino> ReapAllDead()
    {
        var reaped = new List<ChildRhino>();
        foreach (var c in store.ListReady())
        {
            if (IsAlive(c)) continue;
            store.Delete(c.SlotId);
            reaped.Add(c);
        }
        if (reaped.Count > 0)
        {
            log.LogWarning("Reaped {Count} dead slot(s): {Slots}",
                reaped.Count, string.Join(", ", reaped.Select(r => $"'{r.SlotId}' (pid {r.Pid})")));
        }
        return reaped;
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static Process LaunchWindows(string rhinoExe, int port)
    {
        var psi = new ProcessStartInfo
        {
            FileName = rhinoExe,
            Arguments = $"/nosplash /runscript=\"_RhinoMCP {port} _Enter\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
        };

        return Process.Start(psi)
            ?? throw new InvalidOperationException($"Process.Start returned null for {rhinoExe}");
    }

    // Launches Rhino.app via `open -a`. We don't get a usable Process handle back —
    // `open` exits immediately and the Rhino pid is resolved later by lsof against
    // the listening port. ArgumentList ensures the runscript value (which contains
    // spaces) survives as a single argv element.
    private static void LaunchMac(string appPath, int port)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/open",
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(appPath);
        psi.ArgumentList.Add("--args");
        psi.ArgumentList.Add("-nosplash");
        psi.ArgumentList.Add($"-runscript=_RhinoMCP {port} _Enter");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start `open -a {appPath}`.");
        // `open` returns immediately once the app is launched; bounded wait is just defensive.
        proc.WaitForExit(10_000);
    }

    private static int FindPidListeningOnPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/sbin/lsof",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-iTCP:" + port);
            psi.ArgumentList.Add("-sTCP:LISTEN");
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add("-P");

            using var proc = Process.Start(psi);
            if (proc is null) return 0;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5_000);
            foreach (var line in output.Split('\n'))
            {
                if (int.TryParse(line.Trim(), out var pid)) return pid;
            }
        }
        catch
        {
            /* fall through */
        }
        return 0;
    }

    private static bool WaitForPort(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsPortListening(port)) return true;
            Thread.Sleep(500);
        }
        return false;
    }

    private static bool IsPortListening(int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync("127.0.0.1", port);
            return task.Wait(200) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}

// Thrown by CloseAsync when the caller tries to close an adopted slot. The
// tools layer catches this and turns it into a structured `cannot_close_adopted`
// payload — the agent learns why and we still avoid killing a user-started
// Rhino.
public sealed class AdoptedSlotCloseException(string slotId)
    : InvalidOperationException($"Slot '{slotId}' was adopted from a user-started Rhino and cannot be closed by the router.")
{
    public string SlotId { get; } = slotId;
}

// `Adopted` is set when the router discovered this Rhino via a drop-file
// announcement rather than spawning it. Adopted slots are never killed by the
// router (CloseAll skips them; close_slot refuses) — the user started them, the
// user closes them.
//
// `Status` is internal lifecycle state (launching/ready). It's kept off the
// JSON wire so list_slots output stays the same shape as before — agents
// shouldn't see 'launching' rows because ListReady already filters them out.
public record ChildRhino(
    string SlotId,
    int Port,
    int Pid,
    string Version,
    bool Adopted = false,
    [property: JsonIgnore] string Status = SlotStatus.Ready)
{
    public string Endpoint => $"http://localhost:{Port}";
}
