using System.Diagnostics;
using System.IO;

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
        Servers.Add(doc.RuntimeSerialNumber, server);

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

    // Drop a one-shot announcement into <temp>/rhino-mcp/listeners/ so a router
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

    // Stop the listener bound to the given port, regardless of doc. Used by the
    // router's control channel on Mac to tear down a single slot without
    // affecting other slots sharing the same Rhino process.
    public static bool StopByPort(int port)
    {
        var entry = Servers.FirstOrDefault(kv => kv.Value.Port == port);
        if (entry.Value is null) return false;
        Servers.Remove(entry.Key);
        entry.Value.Stop();
        return true;
    }
}
