using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Rhino.FileIO;

namespace RhMcp;

public static class RhinoMcpHost
{

    private static Dictionary<uint, McpServer> Servers { get; } = new();

    // UI-thread-only!
    private static Timer? _heartbeat;

    // Re-advertise live listeners on this interval. Lets a spuriously-reaped slot 
    // re-adopt on its own instead of staying gone until the user re-runs MCPStart. 
    // Re-dropping a already-adopted listener is a no-op.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    static RhinoMcpHost()
    {
        RhinoDoc.CloseDocument += CloseServer;
    }

    // A doc that owned a listener is closing (File>New/Open, or a plain close). 
    // Stop the server, otherwise the listener is orphaned.
    private static void CloseServer(object? sender, DocumentEventArgs e)
    {
        if (!Servers.Remove(e.DocumentSerialNumber, out McpServer? server))
            return;
        if (server is not null)
        {
            WriteDeparture(server.Port);
            server.Stop();
        }
        StopHeartbeatIfIdle();
    }

    public static bool HasStarted(RhinoDoc doc) =>
        Servers.TryGetValue(doc.RuntimeSerialNumber, out McpServer? server)
            && (server?.HasStarted ?? false);

    public static bool TryGetPortFor(RhinoDoc doc, out int port)
    {
        port = -1;
        if (!Servers.TryGetValue(doc.RuntimeSerialNumber, out McpServer? server)) return false;
        if (!server.HasStarted) return false;
        port = server.Port;
        return true;
    }

    private const int DefaultPort = 10500;

    // Worked-or-not: true with a bound-then-released free port in `port`, false if the
    // OS could give us nothing. We bind the candidate to port 0 so the OS assigns any
    // free ephemeral port rather than failing when our preferred Max+1 happens to be
    // occupied.
    public static bool TryGetNextPort(out int port)
    {
        int candidate = DefaultPort;
        if (Servers.Count > 0)
        {
            candidate = Servers.Max(s => s.Value.Port) + 1;
        }

        if (TryBindCandidate(candidate, out port))
            return true;

        return TryBindCandidate(0, out port);
    }

    private static bool TryBindCandidate(int candidate, out int port)
    {
        port = default;
        try
        {
            System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, candidate);
            listener.Start();
            port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    public static bool Start(RhinoDoc doc, int port)
    {
        if (HasStarted(doc))
            return true;
        McpServer server = new();
        Servers[doc.RuntimeSerialNumber] = server;

        bool ok = server.Start(doc, port);
        if (ok)
        {
            WriteAnnouncement(port);
            EnsureHeartbeat();
            return true;
        }

        // A failed start must not leave a dead entry (Port set, HasStarted false) behind.
        Servers.Remove(doc.RuntimeSerialNumber);
        return false;
    }

    public static void Stop(RhinoDoc doc)
    {
        if (!Servers.Remove(doc.RuntimeSerialNumber, out McpServer? server))
            return;
        if (server is not null)
        {
            WriteDeparture(server.Port);
            server.Stop();
        }
        StopHeartbeatIfIdle();
    }

    public static bool RestartOnPort(RhinoDoc doc, int port)
    {
        if (port < 1 || port > 65535)
            return false;
        Stop(doc);
        return Start(doc, port);
    }

    // Shared dispatch for both the interactive `MCPStart` command and the
    // hidden `MCPSpawn` autostart path. Writes user-facing status lines.
    public static bool StartOrRestart(RhinoDoc doc, int port, bool quiet = false)
    {
        if (HasStarted(doc))
        {
            if (!RestartOnPort(doc, port))
            {
                if (!quiet)
                {
                    RhinoApp.WriteLine($"[Rhino MCP] Failed to bind port {port}.");
                }
                return false;
            }
            if (!quiet)
            {
                RhinoApp.WriteLine($"[Rhino MCP] Restarted on http://localhost:{port}/");
            }
            return true;
        }

        if (Start(doc, port))
            return true;

        if (!quiet)
        {
            RhinoApp.WriteLine($"[Rhino MCP] MCP server failed to start. Try a different port.");
        }
        return false;
    }


    private static void EnsureHeartbeat()
    {
        _heartbeat ??= new Timer(static _ => Heartbeat(), null, HeartbeatInterval, HeartbeatInterval);
    }

    private static void StopHeartbeatIfIdle()
    {
        if (Servers.Count == 0)
        {
            _heartbeat?.Dispose();
            _heartbeat = null;
        }
    }

    private static void Heartbeat()
    {
        // Heartbeat must be touched from UI thread
        RhinoApp.InvokeOnUiThread(new Action(static () =>
        {
            foreach (McpServer server in Servers.Values)
            {
                if (server.HasStarted)
                    WriteAnnouncement(server.Port);
            }
        }), null);
    }

    // Drop a one-shot announcement into <temp>/rhino-mcp-listeners/ so a router
    // running on this machine can discover and adopt this listener without us
    // having to know whether one is up. The router consumes (probes + deletes)
    // the file on its next scan; if no router ever sees it, temp sweep collects
    // it eventually. See router's RhinoManager.ScanAnnouncements.
    private static void WriteAnnouncement(int port)
    {
        try
        {
            var dir = ListenerDropDir();
            Directory.CreateDirectory(dir);
            var pid = Process.GetCurrentProcess().Id;
            var version = RhinoApp.Version.Major.ToString();
            var path = Path.Combine(dir, $"{pid}-{port}.json");
            var tmp = path + ".tmp";
            var json = JsonSerializer.Serialize(new { v = 1, pid, port, version });
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Failed to write listener announcement: {ex.Message}");
        }
    }

    // Inverse of the announcement: when a listener goes down cleanly (doc closed,
    // MCPStart restart) drop a one-shot tombstone so a running router prunes the
    // slot promptly and reports a user close as such, not a crash. Best-effort:
    // if no router ever reads it, the router's own port probe reaps the slot.
    // Filename MUST match the router's RhinoManager departure handling.
    private static void WriteDeparture(int port)
    {
        try
        {
            string dir = ListenerDropDir();
            Directory.CreateDirectory(dir);
            int pid = Process.GetCurrentProcess().Id;
            string version = RhinoApp.Version.Major.ToString();
            string path = Path.Combine(dir, $"{pid}-{port}.gone");
            string tmp = path + ".tmp";
            string json = JsonSerializer.Serialize(new { v = 1, pid, port, version });
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Failed to write listener departure: {ex.Message}");
        }
    }

    // Shared with the router via the linked RouterPaths source file, so the
    // drop-dir contract has one owner and can't drift between the two assemblies.
    private static string ListenerDropDir() => RhMcp.Router.RouterPaths.ListenersDir;

    // Stop the listener bound to the given port and close its associated doc
    // without keeping any save artefacts. Used by the router's control channel
    // on Mac to tear down a single slot without affecting other slots sharing
    // the same Rhino process. The router only calls this for slots it spawned
    // (adopted slots are refused upstream), so discarding the doc is safe.
    //
    // Mac's `_-Close` command matches docs by their on-disk path (see
    // src4/rhino4/commands/cmdFileIO.cpp) and is the only way to programmatically
    // close a non-headless doc — RhinoDoc.Dispose is a no-op for them. We give
    // the doc a temp path via WriteFile so the command can find it, then delete
    // that file once Cocoa's deferred close has run.
    public static bool StopByPort(int port)
    {
        KeyValuePair<uint, McpServer> entry = Servers.FirstOrDefault(kv => kv.Value.Port == port);
        if (entry.Value is null)
            return false;

        Servers.Remove(entry.Key);
        var docSerial = entry.Key;
        entry.Value.Stop();
        StopHeartbeatIfIdle();

        var doc = RhinoDoc.FromRuntimeSerialNumber(docSerial);
        if (doc is null)
            return true;

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"rh-mcp-slot-close-{docSerial}-{Guid.NewGuid():N}.3dm");
        try
        {
            doc.Modified = false;
            doc.WriteFile(tempPath, new FileWriteOptions
            {
                SuppressDialogBoxes = true,
                WriteUserData = true,
                UpdateDocumentPath = true,
            });
            RhinoApp.RunScript(docSerial, $"_-Close \"{tempPath}\"", false);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Slot doc close failed for port {port}: {ex.Message}");
            return true;
        }

        // Mac defers the doc close via Cocoa performSelector:afterDelay:0.1.
        // Wait past that, then delete the temp file. Fire-and-forget — we
        // don't want to block the router's HTTP response.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000).ConfigureAwait(false);
            try
            { File.Delete(tempPath); }
            catch { /* OS temp sweep will get it */ }
        });

        return true;
    }
}
