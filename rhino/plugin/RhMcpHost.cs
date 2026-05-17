using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Rhino.FileIO;

namespace RhMcp;

public static class RhinoMcpHost
{

    private static Dictionary<uint, McpServer> Servers { get; } = new();

    static RhinoMcpHost()
    {
        RhinoDoc.CloseDocument += CloseServer;
    }

    private static void CloseServer(object? sender, DocumentEventArgs e)
    {
        Servers.Remove(e.DocumentSerialNumber);
    }

    public static bool HasStarted(RhinoDoc doc) => Servers.TryGetValue(doc.RuntimeSerialNumber, out McpServer? server) && (server?.HasStarted ?? false);

    private const int DefaultPort = 10500;
    public static int GetNextPort()
    {
        if (Servers.Count <= 0) return DefaultPort;
        return Servers.Max(s => s.Value.Port) + 1;
    }

    public static bool Start(RhinoDoc doc, int port)
    {
        if (HasStarted(doc)) return true;
        McpServer server = new();
        Servers[doc.RuntimeSerialNumber] = server;

        var ok = server.Start(doc, port);
        if (ok) WriteAnnouncement(port);
        return ok;
    }

    public static void Stop(RhinoDoc doc)
    {
        if (!Servers.TryGetValue(doc.RuntimeSerialNumber, out McpServer? server)) return;
        Servers.Remove(doc.RuntimeSerialNumber);
        server?.Stop();
    }

    public static bool RestartOnPort(RhinoDoc doc, int port)
    {
        if (port < 1 || port > 65535) return false;
        // TODO : Check no other server is using the port and report to user
        Stop(doc);
        Start(doc, port);
        return true;
    }

    // Shared dispatch for both the interactive `RhinoMCP` command and the
    // hidden `StartMCP` autostart path. Writes user-facing status lines.
    public static bool StartOrRestart(RhinoDoc doc, int port)
    {
        if (HasStarted(doc))
        {
            if (!RestartOnPort(doc, port))
            {
                RhinoApp.WriteLine($"[Rhino MCP] Failed to bind port {port}.");
                return false;
            }
            RhinoApp.WriteLine($"[Rhino MCP] Restarted on http://localhost:{port}/");
            return true;
        }

        if (Start(doc, port)) return true;

        RhinoApp.WriteLine($"[Rhino MCP] MCP server failed to start. Try a different port.");
        return false;
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
            var dir = Path.Combine(Path.GetTempPath(), "rhino-mcp", "listeners");
            Directory.CreateDirectory(dir);
            var pid = Process.GetCurrentProcess().Id;
            var version = RhinoApp.Version.Major.ToString();
            var path = Path.Combine(dir, $"{pid}-{port}.json");
            var tmp = path + ".tmp";
            var json = JsonSerializer.Serialize(new { v = 1, pid, port, version });
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Failed to write listener announcement: {ex.Message}");
        }
    }

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
        var entry = Servers.FirstOrDefault(kv => kv.Value.Port == port);
        if (entry.Value is null) return false;
        var docSerial = entry.Key;
        Servers.Remove(docSerial);
        entry.Value.Stop();

        var doc = RhinoDoc.FromRuntimeSerialNumber(docSerial);
        if (doc is null) return true;

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
            try { File.Delete(tempPath); } catch { /* OS temp sweep will get it */ }
        });

        return true;
    }
}
