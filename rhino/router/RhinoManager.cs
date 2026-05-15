using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

// Spawns, tracks, and tears down Rhino "slots".
//
// Process model differs by OS:
//   Windows: one OS process per slot. Each child gets its own RhinoDoc on its
//            own private port that only the router talks to.
//   macOS:   at most one OS process per Rhino version (Rhino is single-instance
//            per bundle id). The first slot for a version launches the .app;
//            subsequent slots for the same version share that pid and ask the
//            existing listener to spawn another doc + listener via the
//            _router_spawn_listener control tool.
public class RhinoManager(
    RhinoLocator locator,
    RouterConfig config,
    RhinoControlClient control,
    ILogger<RhinoManager> log)
{
    private readonly Dictionary<string, ChildRhino> _children = new();
    private readonly object _lock = new();

    // Serialises GetOrCreateDefault so two slot-less tool calls arriving at once
    // don't both spawn their own default Rhino.
    private readonly SemaphoreSlim _defaultGate = new(1, 1);

    // Serialises Mac spawn flow so concurrent SpawnAsync calls for the same
    // version can't both decide there's no lead listener and both try to launch
    // a fresh Rhino. Cheap (only Mac uses it) and we never await long-running
    // work outside it on Mac.
    private readonly SemaphoreSlim _macSpawnGate = new(1, 1);

    // Reserved slot id for the auto-spawned default Rhino used by tool calls that
    // don't pass an explicit slot.
    public const string DefaultSlotId = "default";

    // Children walk forward from the conventional RhinoMCP port (10500) to find a free one.
    // A user who started a Rhino manually via `_RhinoMCP` will already be on 10500, so the
    // router-spawned children land on 10501, 10502, ... without colliding.
    private const int ChildPortBase = 10500;
    private const int SpawnTimeoutSeconds = 60;

    public Task<ChildRhino> SpawnAsync(string? version = null, CancellationToken ct = default) =>
        SpawnInternalAsync(version ?? config.DefaultVersion, AnimalNames.Next(), ct);

    // Lazily return the default slot, spawning a Rhino for it if one doesn't already exist.
    // Called by ProxyDispatcher when a tool is invoked without an explicit slot.
    // If the user started a Rhino manually and ran `_RhinoMCP`, the drop-file scan
    // adopts it and we reuse that session instead of spawning a parallel one — the
    // user's manual launch is the strongest possible signal that they want this
    // Rhino used.
    public async Task<ChildRhino> GetOrCreateDefaultAsync(CancellationToken ct = default)
    {
        ScanAnnouncements();

        lock (_lock)
        {
            if (TryPickDefault(out var existing)) return existing;
        }

        await _defaultGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Re-check under the gate — another caller may have spawned (or an
            // announcement may have been adopted) while we waited.
            lock (_lock)
            {
                if (TryPickDefault(out var existing)) return existing;
            }
            return await SpawnInternalAsync(config.DefaultVersion, DefaultSlotId, ct).ConfigureAwait(false);
        }
        finally
        {
            _defaultGate.Release();
        }
    }

    // Caller must hold _lock. Picks the slot a slot-less tool call should route
    // to: the reserved "default" slot first, otherwise any adopted user-started
    // Rhino on the configured version.
    private bool TryPickDefault(out ChildRhino slot)
    {
        if (_children.TryGetValue(DefaultSlotId, out var def))
        {
            slot = def;
            return true;
        }
        var adopted = _children.Values.FirstOrDefault(
            c => c.Adopted && c.Version == config.DefaultVersion);
        if (adopted is not null)
        {
            slot = adopted;
            return true;
        }
        slot = default!;
        return false;
    }

    private async Task<ChildRhino> SpawnInternalAsync(string version, string slotId, CancellationToken ct)
    {
        // Prune stale slot entries before deciding what to do. Without this, a Mac
        // slot whose Rhino crashed earlier in the session stays in the registry,
        // and SpawnMacAsync would try to reuse its dead control endpoint —
        // surfacing as a confusing "Connection refused (localhost:10500)" deep
        // inside RhinoControlClient.SpawnListenerAsync.
        ReapAllDead();

        var rhinoExe = locator.ResolveRhinoExe(version);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var port = PickFreePort();
            log.LogInformation("Spawning Rhino {Version} as slot '{Slot}' on port {Port} (exe: {Exe})",
                version, slotId, port, rhinoExe);
            var proc = LaunchWindows(rhinoExe, port);
            return WaitAndRegister(slotId, version, port, proc.Id);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return await SpawnMacAsync(rhinoExe, version, slotId, ct).ConfigureAwait(false);
        }

        throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
    }

    private async Task<ChildRhino> SpawnMacAsync(string appPath, string version, string slotId, CancellationToken ct)
    {
        await _macSpawnGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ChildRhino? lead;
            lock (_lock) lead = _children.Values.FirstOrDefault(c => c.Version == version);

            if (lead is null)
            {
                var port = PickFreePort();
                log.LogInformation("Launching Rhino {Version} as slot '{Slot}' on port {Port} (app: {App})",
                    version, slotId, port, appPath);
                LaunchMac(appPath, port);
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
                var first = new ChildRhino(slotId, port, pid, version);
                lock (_lock) _children[slotId] = first;
                log.LogInformation("Slot '{Slot}' ready: pid {Pid}, port {Port}", slotId, pid, port);
                return first;
            }

            log.LogInformation("Mac: reusing Rhino {Version} (pid {Pid}) for slot '{Slot}'",
                version, lead.Pid, slotId);
            var newPort = await control.SpawnListenerAsync(lead.Endpoint, ct).ConfigureAwait(false);
            var child = new ChildRhino(slotId, newPort, lead.Pid, version);
            lock (_lock) _children[slotId] = child;
            log.LogInformation("Slot '{Slot}' ready: pid {Pid} (shared), port {Port}", slotId, lead.Pid, newPort);
            return child;
        }
        finally
        {
            _macSpawnGate.Release();
        }
    }

    private ChildRhino WaitAndRegister(string slotId, string version, int port, int pid)
    {
        if (!WaitForPort(port, TimeSpan.FromSeconds(SpawnTimeoutSeconds)))
        {
            try { Process.GetProcessById(pid).Kill(); } catch { /* best effort */ }
            throw new TimeoutException(
                $"Rhino {version} (pid {pid}) did not bind port {port} within {SpawnTimeoutSeconds}s. " +
                $"Possible causes: plugin missing, plugin failed to init, license dialog, slow disk.");
        }
        var child = new ChildRhino(slotId, port, pid, version);
        lock (_lock) _children[slotId] = child;
        log.LogInformation("Slot '{Slot}' ready: pid {Pid}, port {Port}", slotId, pid, port);
        return child;
    }

    public async Task<bool> CloseAsync(string slotId, CancellationToken ct = default)
    {
        ChildRhino? child;
        lock (_lock)
        {
            if (!_children.TryGetValue(slotId, out child)) return false;
        }

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
            ChildRhino? sibling;
            lock (_lock)
            {
                sibling = _children.Values.FirstOrDefault(c => c.Pid == child.Pid && c.SlotId != slotId);
            }

            if (sibling is not null)
            {
                log.LogInformation("Closing slot '{Slot}' listener on port {Port} (pid {Pid} shared with '{Sibling}')",
                    slotId, child.Port, child.Pid, sibling.SlotId);
                try
                {
                    await control.CloseListenerAsync(sibling.Endpoint, child.Port, ct).ConfigureAwait(false);
                    lock (_lock) _children.Remove(slotId);
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

        lock (_lock) _children.Remove(slotId);
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
        // Shutdown path: kill each unique pid once. No control-channel niceties —
        // we're tearing everything down anyway, and multiple slots may share a pid on Mac.
        // Adopted slots are skipped: the user owns those Rhinos, so router shutdown
        // must not take them down.
        string[] ids;
        lock (_lock) ids = _children.Keys.ToArray();

        var killed = new HashSet<int>();
        foreach (var id in ids)
        {
            ChildRhino? c;
            lock (_lock)
            {
                if (!_children.TryGetValue(id, out c)) continue;
                if (c.Adopted) { _children.Remove(id); continue; }
                _children.Remove(id);
            }
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

    public IReadOnlyCollection<ChildRhino> List()
    {
        lock (_lock) return _children.Values.ToArray();
    }

    public ChildRhino? Get(string slotId)
    {
        lock (_lock) return _children.GetValueOrDefault(slotId);
    }

    // Walk the listener-announcement drop directory and adopt any plugin-side
    // Rhino we don't already know about. The plugin writes one file per
    // listener-bind into <temp>/rhino-mcp-listeners; we treat each file as a
    // one-shot "look at me" doorbell — consume by deleting it whether or not
    // adoption succeeded. Stale files (port no longer listening) are dropped
    // silently; files for a pid+port we already track are deleted as a no-op so
    // the Mac _router_spawn_listener path remains idempotent.
    public void ScanAnnouncements()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rhino-mcp-listeners");
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

                bool alreadyKnown;
                lock (_lock)
                {
                    alreadyKnown = _children.Values.Any(c => c.Pid == ann.Pid && c.Port == ann.Port);
                }

                if (alreadyKnown)
                {
                    // Mac _router_spawn_listener case (and any other duplicate) —
                    // file is harmless once consumed.
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

                var slotId = AnimalNames.Next();
                var child = new ChildRhino(slotId, ann.Port, ann.Pid, ann.Version ?? "?", Adopted: true);
                lock (_lock) _children[slotId] = child;
                log.LogInformation("Adopted user-started Rhino {Version} as slot '{Slot}' (pid {Pid}, port {Port})",
                    child.Version, slotId, ann.Pid, ann.Port);
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
        if (!IsProcessAlive(c.Pid)) return false;
        if (!IsPortListening(c.Port)) return false;
        return true;
    }

    // Probe one slot; if dead, drop it from the registry. Returns true if reaped.
    // Used by ProxyDispatcher when an HTTP call to a slot fails with a connection
    // error, so the next tool call doesn't keep hitting the same dead slot.
    public bool TryReapDead(string slotId)
    {
        ChildRhino? c;
        lock (_lock)
        {
            if (!_children.TryGetValue(slotId, out c)) return false;
        }
        if (IsAlive(c)) return false;
        lock (_lock) _children.Remove(slotId);
        log.LogWarning("Reaped dead slot '{Slot}' (pid {Pid}, port {Port}, Rhino {Version})",
            c.SlotId, c.Pid, c.Port, c.Version);
        return true;
    }

    // Probe every slot, drop the dead ones, return what was reaped. Called by
    // list_slots so a silently-crashed Rhino doesn't appear healthy until the
    // next tool call.
    public IReadOnlyCollection<ChildRhino> ReapAllDead()
    {
        ChildRhino[] snapshot;
        lock (_lock) snapshot = _children.Values.ToArray();

        var reaped = new List<ChildRhino>();
        foreach (var c in snapshot)
        {
            if (IsAlive(c)) continue;
            lock (_lock)
            {
                // Recheck under lock — could have been removed by a concurrent close.
                if (_children.TryGetValue(c.SlotId, out var current) && ReferenceEquals(current, c))
                {
                    _children.Remove(c.SlotId);
                    reaped.Add(c);
                }
            }
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

    private int PickFreePort()
    {
        var taken = new HashSet<int>();
        lock (_lock)
        {
            foreach (var c in _children.Values) taken.Add(c.Port);
        }

        for (int p = ChildPortBase; p < 65000; p++)
        {
            if (taken.Contains(p)) continue;
            if (!IsPortListening(p)) return p;
        }
        throw new InvalidOperationException("No free ports available in spawn range.");
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
public record ChildRhino(string SlotId, int Port, int Pid, string Version, bool Adopted = false)
{
    public string Endpoint => $"http://localhost:{Port}";
}
