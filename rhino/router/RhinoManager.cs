using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

// Spawns, tracks, and tears down Rhino "slots". State lives in SlotStore
// (SQLite) so concurrent router processes can't race on port allocation or
// duplicate-spawn the Mac shared-process lead.
//
// Windows: one OS process per slot, each on its own private port.
// macOS:   one OS process per Rhino version (Rhino is single-instance per
//          bundle id). First slot launches the .app; later slots share the pid
//          and ask the existing listener to spawn another doc+listener via
//          _router_spawn_listener. Leader election happens in SlotStore.Reserve.
public class RhinoManager(
    RouterConfig config,
    RhinoControlClient control,
    SlotStore store,
    ILogger<RhinoManager> log)
{
    // Manually-started Rhino lives on 10500; children walk forward from there.
    private const int ChildPortBase = 10500;
    private int StartupTimeoutSeconds { get; } = config.StartupTimeoutSeconds;
    private static readonly TimeSpan StaleLaunchingMaxAge = TimeSpan.FromSeconds(90);

    // Liveness-probe connect budget. Generous enough that a healthy localhost listener
    // answers well inside it, so a slow connect is the exception (and inconclusive).
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1);

    private readonly int _routerPid = Environment.ProcessId;

    // The slot this session last used. In-memory is the right scope: the router
    // process lives and dies with the MCP session.
    private string? ActiveSlotId { get; set; }

    public void SetActiveSlot(string slotId) => ActiveSlotId = slotId;

    public Task<ChildRhino> SpawnAsync(string? version = null, CancellationToken ct = default)
    {
        string resolved = version ?? config.DefaultVersion;
        store.ReapStaleLaunching(StaleLaunchingMaxAge);
        ReapAllDead();
        (SlotReservation reservation, string slotId) = store.ReserveNewNamed(resolved, _routerPid);
        return DispatchReservationAsync(resolved, slotId, reservation, ct);
    }

    // Resolves the Rhino for a slot-less tool call, in priority order: the slot
    // this session last used (sticky), else the oldest Rhino the user opened,
    // else the oldest this router spawned, else spawn the configured default.
    // `requiredVersion` is null for generic tools and "WIP" for GH2 tools, which
    // restricts every reuse step to a compatible Rhino and spawns "WIP" if none.
    public async Task<(ChildRhino Child, bool WasNewlySpawned)> GetOrCreateDefaultAsync(
        string? requiredVersion = null, CancellationToken ct = default)
    {
        ScanAnnouncements();

        // A stale pointer (slot closed/crashed) self-heals: the liveness re-check
        // drops it and we fall through to the rest of the ladder.
        string? activeSlotId = ActiveSlotId;
        if (activeSlotId is not null)
        {
            ChildRhino? active = store.Get(activeSlotId);
            if (active is not null && active.Status == SlotStatus.Ready &&
                (requiredVersion is null || VersionMatch.IsCompatible(active.Version, requiredVersion)))
            {
                return (active, false);
            }
        }

        // Pinned: a user-opened WIP announces as "9", so match on compatibility.
        if (requiredVersion is not null)
        {
            ChildRhino? adoptedMatch = store.ListReady()
                .FirstOrDefault(c => c.Adopted && VersionMatch.IsCompatible(c.Version, requiredVersion));
            if (adoptedMatch is not null) return (adoptedMatch, false);

            ChildRhino? mineMatch = store.ListAllOwnedBy(_routerPid)
                .FirstOrDefault(c => c.Status == SlotStatus.Ready && !c.Adopted &&
                                     VersionMatch.IsCompatible(c.Version, requiredVersion));
            if (mineMatch is not null) return (mineMatch, false);

            ChildRhino spawnedPinned = await SpawnAsync(requiredVersion, ct).ConfigureAwait(false);
            return (spawnedPinned, true);
        }

        ChildRhino? adopted = store.ListReady().FirstOrDefault(c => c.Adopted);
        if (adopted is not null) return (adopted, false);

        ChildRhino? mine = store.ListAllOwnedBy(_routerPid)
            .FirstOrDefault(c => c.Status == SlotStatus.Ready && !c.Adopted);
        if (mine is not null) return (mine, false);

        ChildRhino spawned = await SpawnAsync(config.DefaultVersion, ct).ConfigureAwait(false);
        return (spawned, true);
    }

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

        var ready = await store.WaitForReadyAsync(existing.SlotId, TimeSpan.FromSeconds(StartupTimeoutSeconds), ct).ConfigureAwait(false);
        if (ready is null)
        {
            throw new TimeoutException(
                $"Slot '{existing.SlotId}' was already being launched by another router but never became ready " +
                $"within {StartupTimeoutSeconds}s.");
        }
        return ready;
    }

    private async Task<ChildRhino> LaunchAsLeaderAsync(string version, string slotId, CancellationToken ct)
    {
        try
        {
            string rhinoExe = RhinoLocator.ResolveRhinoExe(version);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                int port = store.ReservePort(slotId, ChildPortBase, IsPortListening);
                log.LogInformation("Spawning Rhino {Version} as slot '{Slot}' on port {Port} (exe: {Exe})",
                    version, slotId, port, rhinoExe);
                Process proc = LaunchWindows(rhinoExe, port);
                switch (WaitForPort(port, TimeSpan.FromSeconds(StartupTimeoutSeconds), proc))
                {
                    case WaitResult.Bound:
                        break;
                    case WaitResult.ProcessDied:
                        throw new TimeoutException(
                            $"Rhino {version} (pid {proc.Id}) exited with code {proc.ExitCode} before binding port {port}. " +
                            $"Possible causes: startup crash, missing runtime dependency, license/EULA refused, " +
                            $"plugin load failure.");
                    case WaitResult.Timeout:
                        // Refresh: MainWindowHandle is cached on first access. Zero handle
                        // means no interactive-desktop access (e.g. spawned from IDE extension host).
                        proc.Refresh();
                        bool hasWindow = proc.MainWindowHandle != IntPtr.Zero;
                        try { proc.Kill(); } catch { /* best effort */ }
                        throw new TimeoutException(hasWindow
                            ? $"Rhino {version} (pid {proc.Id}) has a main window but did not bind port {port} within {StartupTimeoutSeconds}s. " +
                              $"Possible causes: license/EULA dialog blocking, plugin failed to load, runscript stuck."
                            : $"Rhino {version} (pid {proc.Id}) is running but never created a main window. " +
                              $"Likely the router was launched from a process context without interactive-desktop access " +
                              $"(IDE extension host, service, or session-0). Try launching the router from a regular terminal to confirm.");
                }
                store.MarkReady(slotId, port, proc.Id);
                log.LogInformation("Slot '{Slot}' ready: pid {Pid}, port {Port}", slotId, proc.Id, port);
                return store.Get(slotId)!;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                int port = store.ReservePort(slotId, ChildPortBase, IsPortListening);
                log.LogInformation("Launching Rhino {Version} as slot '{Slot}' on port {Port} (app: {App})",
                    version, slotId, port, rhinoExe);
                LaunchMac(rhinoExe, port);
                if (WaitForPort(port, TimeSpan.FromSeconds(StartupTimeoutSeconds)) != WaitResult.Bound)
                {
                    throw new TimeoutException(
                        $"Rhino {version} did not bind port {port} within {StartupTimeoutSeconds}s. " +
                        $"Possible causes: plugin missing, plugin failed to init, license dialog, slow disk.");
                }
                int pid = FindPidListeningOnPort(port);
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
            // Free the placeholder so the next caller doesn't wait 90s for the reaper.
            store.Delete(slotId);
            throw;
        }
    }

    // Mac-only: another slot for this version exists. Wait for the leader, then ask
    // its listener to spawn a sibling doc+port.
    private async Task<ChildRhino> LaunchAsFollowerAsync(string version, string slotId, CancellationToken ct)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Windows has no shared-process model; every slot is its own process.
                return await LaunchAsLeaderAsync(version, slotId, ct).ConfigureAwait(false);
            }

            var lead = await WaitForLeadAsync(version, slotId, ct).ConfigureAwait(false);
            int leadPid = lead.Pid ?? throw new InvalidOperationException($"Ready lead slot '{lead.SlotId}' has no pid.");
            log.LogInformation("Mac: reusing Rhino {Version} (pid {Pid}) for slot '{Slot}'",
                version, leadPid, slotId);

            var newPort = await control.SpawnListenerAsync(lead.Endpoint, ct).ConfigureAwait(false);
            store.MarkReady(slotId, newPort, leadPid);
            log.LogInformation("Slot '{Slot}' ready: pid {Pid} (shared), port {Port}", slotId, leadPid, newPort);
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
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(StartupTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var lead = store.FindReadyLead(version, slotId);
            if (lead is not null) return lead;
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Slot '{slotId}' waited {StartupTimeoutSeconds}s for a sibling Rhino {version} to become ready but none did.");
    }

    public async Task<bool> CloseAsync(string slotId, CancellationToken ct = default)
    {
        var child = store.Get(slotId);
        if (child is null) return false;

        if (child.Adopted)
        {
            // User started this Rhino, so the router doesn't get to kill it.
            throw new AdoptedSlotCloseException(slotId);
        }

        // A launching placeholder has no pid/port to act on; the leader/follower
        // launch path deletes it on failure, but a close racing the launch just
        // drops the placeholder so it doesn't linger until the reaper.
        if (child.Pid is not { } pid)
        {
            log.LogInformation("Closing slot '{Slot}' while still launching; dropping placeholder.", slotId);
            store.Delete(slotId);
            return true;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // If siblings share this pid, close just our listener and leave Rhino running.
            var sibling = store.FindSiblingByPid(slotId, pid);
            if (sibling is not null)
            {
                log.LogInformation("Closing slot '{Slot}' listener on port {Port} (pid {Pid} shared with '{Sibling}')",
                    slotId, child.Port, pid, sibling.SlotId);
                try
                {
                    await control.CloseListenerAsync(sibling.Endpoint, child.Port!.Value, ct).ConfigureAwait(false);
                    store.Delete(slotId);
                    return true;
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Failed to close listener for slot '{Slot}' via control channel.", slotId);
                    return false;
                }
            }
            // Last slot — fall through to cooperative quit.
        }

        // Cooperative shutdown: ask Rhino to quit itself via _Exit, then wait
        // for the OS process to actually exit. SIGKILL is reserved for the
        // case where graceful quit doesn't land in time — it's reliable but
        // leaves the TCP listener alive briefly, which races ScanAnnouncements.
        log.LogInformation("Closing slot '{Slot}' cooperatively (pid {Pid})", slotId, pid);
        try
        {
            await control.QuitAppAsync(child.Endpoint, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Quit-app control call for slot '{Slot}' failed; falling through to kill.", slotId);
        }

        if (await WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false))
        {
            store.Delete(slotId);
            log.LogInformation("Slot '{Slot}' exited gracefully (pid {Pid})", slotId, pid);
            return true;
        }

        log.LogWarning("Slot '{Slot}' did not exit within timeout; killing pid {Pid}", slotId, pid);
        store.Delete(slotId);
        try
        {
            Process.GetProcessById(pid).Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* already exited */ }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to kill slot '{Slot}' (pid {Pid})", slotId, pid);
        }
        // SIGKILL is async; wait for the OS to reap the pid so callers don't
        // observe a 'closed' slot whose process+listener are still around.
        await WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> WaitForProcessExitAsync(int pid, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsProcessAlive(pid)) return true;
            try { await Task.Delay(100, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return !IsProcessAlive(pid); }
        }
        return !IsProcessAlive(pid);
    }

    // Shutdown path: kill each unique pid this router owns. Adopted slots and slots
    // owned by other routers are skipped.
    public void CloseAll()
    {
        var owned = store.ListAllOwnedBy(_routerPid);
        HashSet<int> killed = new ();
        foreach (ChildRhino? c in owned)
        {
            if (c.Adopted) { store.Delete(c.SlotId); continue; }
            store.Delete(c.SlotId);
            if (c.Pid is not { } pid || pid <= 0) continue;
            if (!killed.Add(pid)) continue;
            try
            {
                Process.GetProcessById(pid).Kill(entireProcessTree: true);
            }
            catch (ArgumentException) { /* already exited */ }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to kill pid {Pid} during CloseAll", pid);
            }
        }
    }

    public IReadOnlyCollection<ChildRhino> List() => store.ListReady();

    public ChildRhino? Get(string slotId)
    {
        var c = store.Get(slotId);
        return c is null || c.Status != SlotStatus.Ready ? null : c;
    }

    // Status-agnostic existence check. `Get` filters to Ready slots so callers
    // routing tool calls don't accidentally dispatch into a half-launched slot;
    // `close_slot` needs to see launching rows too, otherwise it falsely reports
    // slot_not_found for a slot another router is still spawning.
    public bool Has(string slotId) => store.Get(slotId) is not null;

    // Tombstones the plugin drops when a listener closes cleanly. Siblings to the
    // *.json announcements in the same dir; MUST match RhMcpHost.WriteDeparture.
    private const string DepartureGlob = "*.gone";

    // Adopt any user-started Rhino announced via the drop directory. Each file is a
    // one-shot doorbell, always deleted, success or not. Departures are consumed
    // first so a just-closed listener doesn't get re-adopted from a stale *.json.
    public void ScanAnnouncements()
    {
        ConsumeDepartures();

        var dir = RouterPaths.ListenersDir;
        if (!Directory.Exists(dir)) return;

        string[] files;
        try { files = Directory.GetFiles(RouterPaths.ListenersDir, "*.json"); }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to enumerate listener-announcement dir {Dir}", RouterPaths.ListenersDir);
            return;
        }
        if (files.Length <= 0) return;

        foreach (string file in files)
        {
            try
            {
                Announcement? ann;
                try
                {
                    string json = File.ReadAllText(file);
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

                // Slots are keyed by version; GetOrCreateDefaultAsync matches on it. A
                // version-less announcement could only be stored under a placeholder that
                // matches nothing, occupying a name/pid/port while staying unroutable. Skip
                // it at the boundary rather than adopting an unroutable slot.
                if (ann.Version is not { } version)
                {
                    log.LogWarning("Announcement for pid {Pid} port {Port} has no Rhino version; skipping adoption",
                        ann.Pid, ann.Port);
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

                var slotId = store.AdoptIfNew(version, ann.Port, ann.Pid, _routerPid);
                if (slotId is not null)
                {
                    log.LogInformation("Adopted user-started Rhino {Version} as slot '{Slot}' (pid {Pid}, port {Port})",
                        version, slotId, ann.Pid, ann.Port);
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

    // Consume graceful-close tombstones: a listener that went down cleanly (doc
    // closed, MCPStart restart) drops a *.gone file so we prune its slot here
    // instead of discovering it dead via a probe and crying crash. One-shot
    // doorbells, always deleted, success or not.
    public void ConsumeDepartures()
    {
        string dir = RouterPaths.ListenersDir;
        if (!Directory.Exists(dir)) return;

        string[] files;
        try { files = Directory.GetFiles(dir, DepartureGlob); }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to enumerate listener-departure dir {Dir}", dir);
            return;
        }

        foreach (string file in files)
        {
            try
            {
                Departure? dep;
                try
                {
                    string json = File.ReadAllText(file);
                    dep = JsonSerializer.Deserialize(json, RouterJsonContext.Default.Departure);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Bad departure file {File}; deleting", file);
                    TryDelete(file);
                    continue;
                }

                if (dep is not null && dep.Port > 0)
                {
                    foreach (string slotId in store.DeleteByListener(dep.Pid, dep.Port))
                    {
                        log.LogInformation("Pruned slot '{Slot}' (pid {Pid}, port {Port}, Rhino {Version}) after graceful close",
                            slotId, dep.Pid, dep.Port, dep.Version ?? "?");
                    }
                }
                TryDelete(file);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Unexpected failure processing departure {File}", file);
                TryDelete(file);
            }
        }
    }

    // Did this listener leave a graceful-close tombstone? If so, consume it and
    // prune the slot. Lets the dispatcher report a user close as such, rather
    // than a crash, when a tool call lands on a just-closed slot.
    public bool TryConsumeDeparture(int pid, int port)
    {
        string path = Path.Combine(RouterPaths.ListenersDir, $"{pid}-{port}.gone");
        if (!File.Exists(path)) return false;
        TryDelete(path);
        store.DeleteByListener(pid, port);
        return true;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* next scan will retry */ }
    }

    private static bool ShouldReap(ChildRhino c)
    {
        if (c.Status != SlotStatus.Ready) return false; // launching rows are pending, not dead
        if (c.Pid is not { } pid || c.Port is not { } port)
            throw new InvalidOperationException($"Ready slot '{c.SlotId}' is missing pid/port.");
        if (!IsProcessAlive(pid)) return true;
        // A refused connection is definitively dead; a timeout/error is inconclusive
        // and gets the benefit of the doubt (not reaped) until a later probe is sure.
        return ProbePort(port) == PortProbe.Refused;
    }

    public bool TryReapDead(string slotId)
    {
        var c = store.Get(slotId);
        if (c is null) return false;
        if (!ShouldReap(c)) return false;
        store.Delete(slotId);
        log.LogWarning("Reaped dead slot '{Slot}' (pid {Pid}, port {Port}, Rhino {Version})",
            c.SlotId, c.Pid, c.Port, c.Version);
        return true;
    }

    public IReadOnlyCollection<ChildRhino> ReapAllDead()
    {
        var reaped = new List<ChildRhino>();
        foreach (var c in store.ListReady())
        {
            if (!ShouldReap(c)) continue;
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

    // Must match MCPSpawnCommand.PortEnvVar in rhino/plugin/MCPSpawnCommand.cs.
    private const string PortEnvVar = "RHINO_MCP_AUTOSTART_PORT";

    // Uses CreateProcess + CREATE_BREAKAWAY_FROM_JOB; see WinSpawn for the rationale.
    private static Process LaunchWindows(string rhinoExe, int port)
    {
        return WinSpawn.Start(
            rhinoExe,
            "/nosplash /runscript=\"_MCPSpawn\"",
            new Dictionary<string, string> { [PortEnvVar] = port.ToString() });
    }

    // `open -a` exits immediately, so we resolve the Rhino pid via lsof later.
    // Port goes through an env var because runscript int args race with command registration.
    private static void LaunchMac(string appPath, int port)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/open",
            UseShellExecute = false,
        };
        psi.Environment[PortEnvVar] = port.ToString();
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(appPath);
        psi.ArgumentList.Add("--args");
        psi.ArgumentList.Add("-nosplash");
        psi.ArgumentList.Add("-runscript=_MCPSpawn");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start `open -a {appPath}`.");
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

    private enum WaitResult { Bound, ProcessDied, Timeout }

    // When `proc` is supplied, also short-circuit on process exit to distinguish
    // a crash from a slow startup. Mac passes null (no Process handle from `open -a`).
    private static WaitResult WaitForPort(int port, TimeSpan timeout, Process? proc = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsPortListening(port)) return WaitResult.Bound;
            if (proc is not null && proc.HasExited) return WaitResult.ProcessDied;
            Thread.Sleep(500);
        }
        return WaitResult.Timeout;
    }

    private static bool IsPortListening(int port) => ProbePort(port) == PortProbe.Listening;

    private enum PortProbe { Listening, Refused, Inconclusive }

    // Probe a localhost port, distinguishing an actively-refused connection
    // (definitively nothing listening) from a timeout/error (inconclusive)
    private static PortProbe ProbePort(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(ProbeTimeout);
            client.ConnectAsync("127.0.0.1", port, cts.Token).AsTask().GetAwaiter().GetResult();
            return client.Connected ? PortProbe.Listening : PortProbe.Inconclusive;
        }
        catch (OperationCanceledException)
        {
            return PortProbe.Inconclusive;
        }
        catch (SocketException se)
        {
            return se.SocketErrorCode == SocketError.ConnectionRefused
                ? PortProbe.Refused
                : PortProbe.Inconclusive;
        }
        catch
        {
            return PortProbe.Inconclusive;
        }
    }
}

// Tools layer catches this and turns it into a structured `cannot_close_adopted` payload.
public sealed class AdoptedSlotCloseException(string slotId)
    : InvalidOperationException($"Slot '{slotId}' was adopted from a user-started Rhino and cannot be closed by the router.")
{
    public string SlotId { get; } = slotId;
}

// `Adopted` slots are never killed by the router — the user started them.
// `Status` is internal lifecycle state, kept off the JSON wire.
// Port/Pid are null on a launching row (the placeholder INSERT omits them) and
// only set once the slot is marked ready. Modelling them as int? keeps a
// half-launched slot from materialising a lying "http://localhost:0" endpoint.
public record ChildRhino(
    string SlotId,
    int? Port,
    int? Pid,
    string Version,
    bool Adopted = false,
    [property: JsonIgnore] SlotStatus Status = SlotStatus.Ready)
{
    public string Endpoint => Port is { } port
        ? $"http://localhost:{port}"
        : throw new InvalidOperationException($"Slot '{SlotId}' has no port yet (status {Status}); its endpoint is not addressable.");
}
