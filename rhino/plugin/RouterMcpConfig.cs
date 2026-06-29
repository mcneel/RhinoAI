using System.IO;
using System.Runtime.InteropServices;

namespace RhMcp;

// One source of truth for the MCP-server entry every host hands its agent: launch the
// bundled rhino-mcp-router over stdio. The router then discovers this session's HTTP
// listener via the on-disk announcement (see RhinoMcpHost.WriteAnnouncement).
internal static class RouterMcpConfig
{
    // The MCP server id; also the tool-name prefix (mcp__rhino__*).
    internal const string ServerName = "rhino";

    // The {"mcpServers":...} shape Claude Code, the connector json, and the test harness accept.
    internal static string Json => JsonSerializer.Serialize(new
    {
        mcpServers = new Dictionary<string, object>
        {
            [ServerName] = new { command = RouterPath },
        },
    }, IndentedOptions);

    // Resolved next to the plugin assembly: …/<plugin>/../router/<rid>/rhino-mcp-router[.exe].
    internal static string RouterPath
    {
        get
        {
            string pluginDir = Path.GetDirectoryName(typeof(RhMcpPlugin).Assembly.Location) ?? string.Empty;
            string routerRoot = Path.GetFullPath(Path.Combine(pluginDir, "..", "router"));
            string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "rhino-mcp-router.exe" : "rhino-mcp-router";
            return Path.Combine(routerRoot, Rid, exe);
        }
    }

    // Indented for the connector dialog's readability; harmless as a single argv string.
    private static JsonSerializerOptions IndentedOptions { get; } =
        new(McpSerializer.Options) { WriteIndented = true };

    private static string Rid
    {
        get
        {
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                _ => "x64",
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";
            return $"linux-{arch}";
        }
    }
}
