using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RhinoAI.Server;

namespace RhinoAI;

internal enum McpConfigFormat
{
    StandardJson,
    OpenCodeJson,
    CodexToml,
}

// Pure, Rhino-free text/JSON surgery for a single MCP server entry, split out of McpClientConfigInstaller so it can be unit-tested.
internal static class McpConfigEdit
{
    public static bool HasEntry(McpConfigFormat format, string content, string serverName) => format switch
    {
        McpConfigFormat.StandardJson => JsonHasEntry(content, "mcpServers", serverName),
        McpConfigFormat.OpenCodeJson => JsonHasEntry(content, "mcp", serverName),
        McpConfigFormat.CodexToml => Regex.IsMatch(content, TomlEntryPattern(serverName), RegexOptions.Multiline),
        _ => false,
    };

    public static string? Add(McpConfigFormat format, string original, string serverName, string command, IReadOnlyDictionary<string, string> env) => format switch
    {
        McpConfigFormat.StandardJson => AddToJson(original, "mcpServers", serverName, () => StandardEntry(command, env)),
        McpConfigFormat.OpenCodeJson => AddToJson(original, "mcp", serverName, () => OpenCodeEntry(command, env)),
        McpConfigFormat.CodexToml => AddToToml(original, serverName, command, env),
        _ => null,
    };

    public static string? Remove(McpConfigFormat format, string original, string serverName) => format switch
    {
        McpConfigFormat.StandardJson => RemoveFromJson(original, "mcpServers", serverName),
        McpConfigFormat.OpenCodeJson => RemoveFromJson(original, "mcp", serverName),
        McpConfigFormat.CodexToml => RemoveFromToml(original, serverName),
        _ => null,
    };

    private static bool JsonHasEntry(string content, string containerKey, string serverName)
    {
        JsonNode? root = JsonNode.Parse(content, documentOptions: LenientJson);
        return root is JsonObject obj
            && obj[containerKey] is JsonObject servers
            && servers.ContainsKey(serverName);
    }

    private static string? AddToJson(string original, string containerKey, string serverName, Func<JsonNode> entry)
    {
        JsonNode? root = JsonNode.Parse(original, documentOptions: LenientJson);
        if (root is not JsonObject obj)
            return null;

        switch (obj[containerKey])
        {
            // Mutate the existing map in place: reassigning an already-parented JsonNode throws.
            case JsonObject servers when servers.ContainsKey(serverName):
                return null;
            case JsonObject servers:
                servers[serverName] = entry();
                break;
            case null:
                obj[containerKey] = new JsonObject { [serverName] = entry() };
                break;
            default:
                return null; // present but not a server map; don't risk clobbering it.
        }

        return obj.ToJsonString(IndentedJson);
    }

    private static string? RemoveFromJson(string original, string containerKey, string serverName)
    {
        JsonNode? root = JsonNode.Parse(original, documentOptions: LenientJson);
        if (root is not JsonObject obj || obj[containerKey] is not JsonObject servers)
            return null;

        servers.Remove(serverName);
        return obj.ToJsonString(IndentedJson);
    }

    // No TOML parser is bundled, so detect the section header textually and append the table if
    // it's absent. A top-level table appended at EOF is always valid TOML regardless of what
    // precedes it, which makes this safe without a full parse.
    private static string? AddToToml(string original, string serverName, string command, IReadOnlyDictionary<string, string> env)
    {
        if (Regex.IsMatch(original, TomlEntryPattern(serverName), RegexOptions.Multiline))
            return null;

        string section =
            $"[mcp_servers.{serverName}]\n" +
            $"command = {TomlBasicString(command)}\n";

        if (env.Count > 0)
        {
            section += $"\n[mcp_servers.{serverName}.env]\n";
            foreach ((string key, string value) in env)
                section += $"{key} = {TomlBasicString(value)}\n";
        }

        string separator = original.Length == 0 || original.EndsWith('\n') ? "\n" : "\n\n";
        return original + separator + section;
    }

    // Must drop the server table's subtables too (the env block), or an orphaned [mcp_servers.x.env] is left.
    private static string RemoveFromToml(string original, string serverName)
    {
        List<string> kept = [];
        bool removing = false;
        foreach (string line in original.Split('\n'))
        {
            if (Regex.IsMatch(line, TomlHeaderPattern))
                removing = Regex.IsMatch(line, TomlTablePattern(serverName));
            if (!removing)
                kept.Add(line);
        }
        return string.Join('\n', kept).TrimEnd() + "\n";
    }

    private static string TomlEntryPattern(string serverName) =>
        @"^\s*\[mcp_servers\.(""?)" + Regex.Escape(serverName) + @"\1\]";

    private static string TomlTablePattern(string serverName) =>
        @"^\s*\[\[?mcp_servers\.(""?)" + Regex.Escape(serverName) + @"\1(?:\.|\])";

    private const string TomlHeaderPattern = """^\s*\[""";

    private static JsonNode StandardEntry(string command, IReadOnlyDictionary<string, string> env)
    {
        JsonObject entry = new() { ["command"] = command };
        if (EnvObject(env) is { } block)
            entry["env"] = block;
        return entry;
    }

    private static JsonNode OpenCodeEntry(string command, IReadOnlyDictionary<string, string> env)
    {
        JsonObject entry = new()
        {
            ["type"] = "local",
            ["command"] = new JsonArray(command),
            ["enabled"] = true,
        };
        if (EnvObject(env) is { } block)
            entry["environment"] = block;
        return entry;
    }

    private static JsonObject? EnvObject(IReadOnlyDictionary<string, string> env)
    {
        if (env.Count == 0)
            return null;
        JsonObject block = new();
        foreach ((string key, string value) in env)
            block[key] = value;
        return block;
    }

    private static string TomlBasicString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static JsonDocumentOptions LenientJson { get; } =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static JsonSerializerOptions IndentedJson { get; } =
        new(McpSerializer.Options) { WriteIndented = true };
}
