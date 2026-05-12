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

        return server.Start(doc, port);
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
