using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp;

// On load, wire the bundled rhino MCP server into the configs of any MCP-aware tools the user
// already has (Claude Code, Cursor, Codex, ...) so those external agents can drive Rhino without
// the user copy-pasting the snippet from MCPConnect. We only touch files that already exist:
// creating dotfiles for tools the user doesn't run would litter the home dir. Injection is
// idempotent (a no-op once a `rhino` entry is present) and never overwrites an existing one.
internal static class McpClientConfigInstaller
{
    private sealed record McpClientConfig(string DisplayName, string RelativePath, McpConfigFormat Format);

    private enum McpConfigFormat
    {
        // mcpServers: { rhino: { command } }  - Claude Code, Cursor, Gemini, Windsurf.
        StandardJson,

        // mcp: { rhino: { type: "local", command: [...], enabled: true } }  - OpenCode's shape.
        OpenCodeJson,

        // [mcp_servers.rhino] command = "..."  - Codex's TOML.
        CodexToml,
    }

    private static IReadOnlyList<McpClientConfig> KnownConfigs { get; } =
    [
        new("Claude Code", ".claude.json", McpConfigFormat.StandardJson),
        new("Cursor", ".cursor/mcp.json", McpConfigFormat.StandardJson),
        new("Gemini CLI", ".gemini/settings.json", McpConfigFormat.StandardJson),
        new("Windsurf", ".codeium/windsurf/mcp_config.json", McpConfigFormat.StandardJson),
        new("OpenCode", ".config/opencode/opencode.json", McpConfigFormat.OpenCodeJson),
        new("Codex", ".codex/config.toml", McpConfigFormat.CodexToml),
    ];

    // Fire-and-forget from OnLoad: the scan is short-lived file IO and must never block startup
    // or surface an exception into OnLoad. Everything below is swallowed and logged at worst.
    public static void InstallInBackground(CancellationToken token) =>
        _ = Task.Run(() => InstallAsync(token), token);

    public static async Task InstallAsync(CancellationToken token)
    {
        foreach (McpClientConfig config in KnownConfigs)
        {
            if (token.IsCancellationRequested)
                return;

            try
            {
                await EnsureRhinoEntryAsync(config, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"skipped {config.DisplayName}: {ex.Message}");
            }
        }
    }

    private static async Task EnsureRhinoEntryAsync(McpClientConfig config, CancellationToken token)
    {
        string path = ResolvePath(config.RelativePath);
        if (!File.Exists(path))
            return;

        string original = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);

        // null means "nothing to write": the entry is already there, or the file isn't a shape we
        // can safely amend. Either way we leave the user's file untouched.
        string? updated = config.Format switch
        {
            McpConfigFormat.StandardJson => AddToJson(original, "mcpServers", StandardEntry),
            McpConfigFormat.OpenCodeJson => AddToJson(original, "mcp", OpenCodeEntry),
            McpConfigFormat.CodexToml => AddToToml(original),
            _ => throw new ArgumentOutOfRangeException(nameof(config), config.Format, "Unknown MCP config format."),
        };

        if (updated is null)
            return;

        await WriteAtomicAsync(path, updated, token).ConfigureAwait(false);
        Log($"wired the Rhino MCP server into {config.DisplayName} ({path})");
    }

    private static string? AddToJson(string original, string containerKey, Func<JsonNode> entry)
    {
        JsonNode? root = JsonNode.Parse(original, documentOptions: LenientJson);
        if (root is not JsonObject obj)
            return null;

        switch (obj[containerKey])
        {
            // Mutate the existing map in place: reassigning an already-parented JsonNode throws.
            case JsonObject servers when servers.ContainsKey(RouterMcpConfig.ServerName):
                return null;
            case JsonObject servers:
                servers[RouterMcpConfig.ServerName] = entry();
                break;
            case null:
                obj[containerKey] = new JsonObject { [RouterMcpConfig.ServerName] = entry() };
                break;
            default:
                return null; // present but not a server map; don't risk clobbering it.
        }

        return obj.ToJsonString(IndentedJson);
    }

    // No TOML parser is bundled, so detect the section header textually and append the table if
    // it's absent. A top-level table appended at EOF is always valid TOML regardless of what
    // precedes it, which makes this safe without a full parse.
    private static string? AddToToml(string original)
    {
        if (Regex.IsMatch(original, """^\s*\[mcp_servers\.("?)rhino\1\]""", RegexOptions.Multiline))
            return null;

        string section =
            $"[mcp_servers.{RouterMcpConfig.ServerName}]\n" +
            $"command = {TomlBasicString(RouterMcpConfig.RouterPath)}\n";

        string separator = original.Length == 0 || original.EndsWith('\n') ? "\n" : "\n\n";
        return original + separator + section;
    }

    private static JsonNode StandardEntry() => new JsonObject
    {
        ["command"] = RouterMcpConfig.RouterPath,
    };

    private static JsonNode OpenCodeEntry() => new JsonObject
    {
        ["type"] = "local",
        ["command"] = new JsonArray(RouterMcpConfig.RouterPath),
        ["enabled"] = true,
    };

    // Same-directory temp then atomic move, so a tool reading the config concurrently never sees a
    // half-written file. A unique temp name avoids two Rhino instances colliding on the same write.
    private static async Task WriteAtomicAsync(string path, string content, CancellationToken token)
    {
        string directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        string temp = Path.Combine(directory, $".rhmcp-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(temp, content, token).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private static string ResolvePath(string relative)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.GetFullPath(Path.Combine(home, relative));
    }

    private static string TomlBasicString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static JsonDocumentOptions LenientJson { get; } =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static JsonSerializerOptions IndentedJson { get; } =
        new(McpSerializer.Options) { WriteIndented = true };

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
