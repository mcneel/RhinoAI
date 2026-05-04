using Rhino;
using Rhino.PlugIns;

namespace RhMcp;

public static class RhMcpHost
{
    public const int DefaultPort = 4862;
    private const string PortKey = "port";

    private static McpServer Server { get; } = new();

    public static bool HasStarted => Server?.HasStarted ?? false;

    public static int Port
    {
        get => RhMcpPlugin.Instance.Settings.GetInteger(PortKey, DefaultPort);
        private set => RhMcpPlugin.Instance.Settings.SetInteger(PortKey, value);
    }

    public static bool Start()
    {
        if (Server.HasStarted) return true;
        return Server.Start();
    }

    public static void Stop()
    {
        Server.Stop();
    }

    public static bool RestartOnPort(int port)
    {
        if (port < 1 || port > 65535) return false;
        Stop();
        Port = port;
        Start();
        return true;
    }
}
