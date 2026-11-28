using System.IO;

namespace Rhino.AI;

// An app counts as present when AppPath exists; Install may create the config file there but never creates a folder.
internal static class McpClientConfigInstaller
{
    internal sealed record McpClient(string DisplayName, string ConfigPath, string AppPath, McpConfigFormat Format);

    internal enum McpInstallState { Missing, Detected, Installed }

    internal enum McpInstallResult { Installed, AlreadyInstalled, Missing, Unsupported, Failed }

    internal enum McpUninstallResult { Uninstalled, NotInstalled, Missing, Unsupported, Failed }

    internal static IReadOnlyList<McpClient> Clients { get; } =
    [
        new("Claude Code", Home(".claude.json"), Home(".claude.json"), McpConfigFormat.StandardJson),
        InFolder("Codex", Home(".codex"), "config.toml", McpConfigFormat.CodexToml),
        InFolder("Antigravity", Home(".gemini", "config"), "mcp_config.json", McpConfigFormat.StandardJson),
        InFolder("Hermes", HermesFolder(), "config.yaml", McpConfigFormat.HermesYaml),
        InFolder("VS Code", Home(".copilot"), "mcp-config.json", McpConfigFormat.CopilotJson),
        InFolder("LM Studio", Home(".lmstudio"), "mcp.json", McpConfigFormat.StandardJson),
        InFolder("Pi", Home(".pi", "agent"), "mcp.json", McpConfigFormat.StandardJson),
    ];

    // Cheap enough to call per grid row on every visit to the Install tab: a File.Exists plus one
    // small read. An unreadable config counts as Detected so the user can still attempt the install
    // (which reports its own error) rather than silently vanishing from the grid.
    public static McpInstallState GetState(McpClient client)
    {
        if (!IsPresent(client))
            return McpInstallState.Missing;
        if (!File.Exists(client.ConfigPath))
            return McpInstallState.Detected;

        try
        {
            return HasRhinoEntry(client, File.ReadAllText(client.ConfigPath))
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
        string path = client.ConfigPath;
        if (!IsPresent(client))
            return McpInstallResult.Missing;

        try
        {
            string original = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            if (HasRhinoEntry(client, original))
                return McpInstallResult.AlreadyInstalled;

            // null means only "shape we can't safely amend": the already-present case is handled above.
            string? updated = McpConfigEdit.Add(client.Format, original, RouterMcpConfig.ServerName, RouterMcpConfig.RouterPath, env);
            if (updated is null)
                return McpInstallResult.Unsupported;

            WriteAtomic(path, updated);
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
        string path = client.ConfigPath;
        if (!IsPresent(client))
            return McpUninstallResult.Missing;
        if (!File.Exists(path))
            return McpUninstallResult.NotInstalled;

        try
        {
            string original = File.ReadAllText(path);
            if (!HasRhinoEntry(client, original))
                return McpUninstallResult.NotInstalled;

            string? updated = McpConfigEdit.Remove(client.Format, original, RouterMcpConfig.ServerName);
            if (updated is null)
                return McpUninstallResult.Unsupported;

            WriteAtomic(path, updated);
            return McpUninstallResult.Uninstalled;
        }
        catch (Exception ex)
        {
            Log($"Failed to update {client.DisplayName}: {ex.Message}");
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
        Move(temp, path, overwrite: true);
    }

    private static void Move(string sourceFileName, string destFileName, bool overwrite)
    {
        if (overwrite && System.IO.File.Exists(destFileName))
        {
            System.IO.File.Delete(destFileName);
        }

        System.IO.File.Move(sourceFileName, destFileName);
    }

    private static bool IsPresent(McpClient client) => File.Exists(client.AppPath) || Directory.Exists(client.AppPath);

    private static McpClient InFolder(string displayName, string folder, string fileName, McpConfigFormat format)
        => new(displayName, Path.Combine(folder, fileName), folder, format);

    private static string HermesFolder() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hermes")
        : Home(".hermes");

    private static string Home(params string[] parts)
        => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), .. parts]);

    private static void Log(string message)
    {
        try
        {
            RhinoApp.WriteLine($"[RhinoAI] {message}");
        }
        catch
        {
        }
    }
}
