using System.Diagnostics;
using System.Net.Sockets;

namespace RhMcp.Router;

// Spawns, tracks, and tears down child Rhino processes.
// Each child runs its own RhinoMCP listener on a private port that only the router talks to.
public class RhinoManager(RhinoLocator locator, RouterConfig config)
{
    private readonly Dictionary<string, ChildRhino> _children = new();
    private readonly object _lock = new();

    // Children get random high ports (above the conventional 10500-10507 user-visible range).
    // Each spawn walks forward from the base to find a free one.
    private const int ChildPortBase = 47100;

    public ChildRhino Spawn(string? version = null)
    {
        version ??= config.DefaultVersion;
        var rhinoExe = locator.ResolveRhinoExe(version);
        var port = PickFreePort();
        var slot = AnimalNames.Next();

        // TODO: launch Rhino with /nosplash /runscript "_-RhinoMCP <port> _Enter"
        // TODO: wait for port to bind (poll up to ~30s).
        // TODO: capture PID, store in _children, return.

        throw new NotImplementedException("Spawn not yet wired up.");
    }

    public bool Close(string slotId)
    {
        lock (_lock)
        {
            if (!_children.TryGetValue(slotId, out var child)) return false;
            // TODO: send `_-Exit _No` via the child's MCP. If that fails, kill the process.
            _children.Remove(slotId);
            return true;
        }
    }

    public void CloseAll()
    {
        // TODO: iterate, close each.
    }

    public IReadOnlyCollection<ChildRhino> List()
    {
        lock (_lock) return _children.Values.ToArray();
    }

    public ChildRhino? Get(string slotId)
    {
        lock (_lock) return _children.GetValueOrDefault(slotId);
    }

    private static int PickFreePort()
    {
        for (int p = ChildPortBase; p < 65535; p++)
        {
            if (!IsPortInUse(p)) return p;
        }
        throw new InvalidOperationException("No free ports available.");
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync("127.0.0.1", port);
            return task.Wait(50) && client.Connected;
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
