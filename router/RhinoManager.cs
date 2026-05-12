using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

// Spawns, tracks, and tears down child Rhino processes.
// Each child runs its own RhinoMCP listener on a private port that only the router talks to.
public class RhinoManager(RhinoLocator locator, RouterConfig config, ILogger<RhinoManager> log)
{
    private readonly Dictionary<string, ChildRhino> _children = new();
    private readonly object _lock = new();

    // Children get random high ports (above the conventional 10500-10507 user-visible range).
    // Each spawn walks forward from the base to find a free one.
    private const int ChildPortBase = 47100;
    private const int SpawnTimeoutSeconds = 60;

    public ChildRhino Spawn(string? version = null)
    {
        version ??= config.DefaultVersion;
        var rhinoExe = locator.ResolveRhinoExe(version);
        var port = PickFreePort();
        var slot = AnimalNames.Next();

        log.LogInformation("Spawning Rhino {Version} as slot '{Slot}' on port {Port} (exe: {Exe})",
            version, slot, port, rhinoExe);

        Process proc;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            proc = LaunchWindows(rhinoExe, port);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // TODO: Mac launch flow. Single-process limitation means subsequent spawns
            // need to drive an existing Rhino via MCP to open a new doc + start MCP.
            // For first spawn, `open -n -a <app> --args -nosplash -runscript=...`.
            throw new PlatformNotSupportedException("macOS spawn not yet implemented.");
        }
        else
        {
            throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
        }

        if (!WaitForPort(port, TimeSpan.FromSeconds(SpawnTimeoutSeconds)))
        {
            try { proc.Kill(); } catch { /* best effort */ }
            throw new TimeoutException(
                $"Rhino {version} (pid {proc.Id}) did not bind port {port} within {SpawnTimeoutSeconds}s. " +
                $"Possible causes: plugin missing, plugin failed to init, license dialog, slow disk.");
        }

        var child = new ChildRhino(slot, port, proc.Id, version);
        lock (_lock) _children[slot] = child;
        log.LogInformation("Slot '{Slot}' ready: pid {Pid}, port {Port}", slot, proc.Id, port);
        return child;
    }

    public bool Close(string slotId)
    {
        ChildRhino? child;
        lock (_lock)
        {
            if (!_children.TryGetValue(slotId, out child)) return false;
            _children.Remove(slotId);
        }

        log.LogInformation("Closing slot '{Slot}' (pid {Pid})", slotId, child.Pid);
        try
        {
            var proc = Process.GetProcessById(child.Pid);
            proc.Kill(entireProcessTree: true);
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
        string[] ids;
        lock (_lock) ids = _children.Keys.ToArray();
        foreach (var id in ids) Close(id);
    }

    public IReadOnlyCollection<ChildRhino> List()
    {
        lock (_lock) return _children.Values.ToArray();
    }

    public ChildRhino? Get(string slotId)
    {
        lock (_lock) return _children.GetValueOrDefault(slotId);
    }

    private static Process LaunchWindows(string rhinoExe, int port)
    {
        var psi = new ProcessStartInfo
        {
            FileName = rhinoExe,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        psi.ArgumentList.Add("/nosplash");
        psi.ArgumentList.Add($"/runscript=_-RhinoMCP {port} _Enter");

        return Process.Start(psi)
            ?? throw new InvalidOperationException($"Process.Start returned null for {rhinoExe}");
    }

    private int PickFreePort()
    {
        // Walk forward from base; skip anything we already own or anything externally listening.
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

public record ChildRhino(string SlotId, int Port, int Pid, string Version)
{
    public string Endpoint => $"http://localhost:{Port}";
}
