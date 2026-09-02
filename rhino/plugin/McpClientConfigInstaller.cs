using System.IO;

namespace RhinoAI;

// Adds the bundled rhino MCP server to the configs of MCP-aware tools the user has (Claude Code,
// Cursor, Codex, ...) so those external agents can drive Rhino without hand-copying the snippet.
// Detection and install are per-client and user-initiated from the Connect dialog's Install tab.
// We never create configs for tools the user doesn't run (that would litter the home dir), and
// injection is idempotent (a no-op once a `rhino` entry is present) and never overwrites one.
// The actual config surgery lives in the Rhino-free McpConfigEdit; this handles disk IO and detection.
internal static class McpClientConfigInstaller
{
    internal sealed record McpClient(string DisplayName, string RelativePath, McpConfigFormat Format);

    internal enum McpInstallState { Missing, Detected, Installed }

    internal enum McpInstallResult { Installed, AlreadyInstalled, Missing, Unsupported, Failed }

    internal enum McpUninstallResult { Uninstalled, NotInstalled, Missing, Unsupported, Failed }

    internal static IReadOnlyList<McpClient> Clients { get; } =
    [
        new("Claude Code", ".claude.json", McpConfigFormat.StandardJson),
        new("Cursor", ".cursor/mcp.json", McpConfigFormat.StandardJson),
        new("Gemini CLI", ".gemini/settings.json", McpConfigFormat.StandardJson),
        new("Windsurf", ".codeium/windsurf/mcp_config.json", McpConfigFormat.StandardJson),
        new("OpenCode", ".config/opencode/opencode.json", McpConfigFormat.OpenCodeJson),
        new("Codex", ".codex/config.toml", McpConfigFormat.CodexToml),
    ];

    // Cheap enough to call per grid row on every visit to the Install tab: a File.Exists plus one
    // small read. An unreadable config counts as Detected so the user can still attempt the install
    // (which reports its own error) rather than silently vanishing from the grid.
    public static McpInstallState GetState(McpClient client)
    {
        string path = ResolvePath(client.RelativePath);
        if (!File.Exists(path))
            return McpInstallState.Missing;

        try
        {
            return HasRhinoEntry(client, File.ReadAllText(path))
                ? McpInstallState.Installed
                : McpInstallState.Detected;
        }
        catch
        {
            return McpInstallState.Detected;
        }
    }

    public static McpInstallResult Install(McpClient client, IReadOnlyDictionary<string, string> env)
    {
        string path = ResolvePath(client.RelativePath);
        if (!File.Exists(path))
            return McpInstallResult.Missing;

        try
        {
            string original = File.ReadAllText(path);
            if (HasRhinoEntry(client, original))
                return McpInstallResult.AlreadyInstalled;

            // null means only "shape we can't safely amend": the already-present case is handled above.
            string? updated = McpConfigEdit.Add(client.Format, original, RouterMcpConfig.ServerName, RouterMcpConfig.RouterPath, env);
            if (updated is null)
                return McpInstallResult.Unsupported;

            WriteAtomic(path, updated);
            Log($"wired the Rhino MCP server into {client.DisplayName} ({path})");
            return McpInstallResult.Installed;
        }
        catch (Exception ex)
        {
            Log($"failed to update {client.DisplayName}: {ex.Message}");
            return McpInstallResult.Failed;
        }
    }

    public static McpUninstallResult Uninstall(McpClient client)
    {
        string path = ResolvePath(client.RelativePath);
        if (!File.Exists(path))
            return McpUninstallResult.Missing;

        try
        {
            string original = File.ReadAllText(path);
            if (!HasRhinoEntry(client, original))
                return McpUninstallResult.NotInstalled;

            string? updated = McpConfigEdit.Remove(client.Format, original, RouterMcpConfig.ServerName);
            if (updated is null)
                return McpUninstallResult.Unsupported;

            WriteAtomic(path, updated);
            Log($"removed the Rhino MCP server from {client.DisplayName} ({path})");
            return McpUninstallResult.Uninstalled;
        }
        catch (Exception ex)
        {
            Log($"failed to update {client.DisplayName}: {ex.Message}");
            return McpUninstallResult.Failed;
        }
    }

    private static bool HasRhinoEntry(McpClient client, string content) =>
        McpConfigEdit.HasEntry(client.Format, content, RouterMcpConfig.ServerName);

    // Same-directory temp then atomic move, so a tool reading the config concurrently never sees a
    // half-written file. A unique temp name avoids two Rhino instances colliding on the same write.
    private static void WriteAtomic(string path, string content)
    {
        string directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        string temp = Path.Combine(directory, $".rhmcp-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    private static string ResolvePath(string relative)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.GetFullPath(Path.Combine(home, relative));
    }

    private static void Log(string message)
    {
        try
        {
            RhinoApp.WriteLine($"[Rhino MCP] {message}");
        }
        catch
        {
        }
    }
}
