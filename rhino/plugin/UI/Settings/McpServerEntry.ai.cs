using System.Text.Json.Nodes;

namespace Rhino.AI.UI.Settings;

internal enum McpServerType { Command, Url }

internal enum McpServerField { Environment, Headers }

internal sealed record NamedServer(string Name, JsonObject Entry);

internal sealed record McpServerFields(
    string Name,
    McpServerType Type,
    string Command,
    string Arguments,
    string Environment,
    string Url,
    string Headers,
    JsonObject Extra)
{
    public static McpServerFields Blank { get; } = new(string.Empty, McpServerType.Command, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, []);

    public bool IsBlank => Extra.Count == 0
        && new[] { Name, Command, Arguments, Environment, Url, Headers }.All(string.IsNullOrWhiteSpace);
}

internal abstract record ServerProblem
{
    private ServerProblem() { }

    public sealed record NameMissing : ServerProblem;
    public sealed record NameTaken(string Name) : ServerProblem;
    public sealed record NameInvalid(string Name) : ServerProblem;
    public sealed record CommandMissing(string Name) : ServerProblem;
    public sealed record UrlInvalid(string Name) : ServerProblem;
    public sealed record ValueInvalid(string Name, string Key) : ServerProblem;
    public sealed record TypeUnsupported(string Name, string Type) : ServerProblem;
    public sealed record SettingsUnused(string Name, IReadOnlyList<string> Keys) : ServerProblem;
    public sealed record BadLine(McpServerField Field, int Line) : ServerProblem;
    public sealed record JsonInvalid(string Message) : ServerProblem;
    public sealed record JsonNotObject : ServerProblem;
    public sealed record NoServers : ServerProblem;
    public sealed record Unnamed : ServerProblem;
    public sealed record NotAnObject(string Name) : ServerProblem;
}

internal abstract record ServerResult<T>
{
    private ServerResult() { }

    public sealed record Ok(T Value) : ServerResult<T>;
    public sealed record Failed(ServerProblem Problem) : ServerResult<T>;
}

internal static class McpServerEntry
{
    private const string CommandKey = "command";
    private const string ArgsKey = "args";
    private const string EnvKey = "env";
    private const string UrlKey = "url";
    private const string HeadersKey = "headers";
    private const string TypeKey = "type";
    private const string WrapperKey = "mcpServers";
    private const string StdioType = "stdio";
    private const string HttpType = "http";

    private static JsonSerializerOptions Indented { get; } = new() { WriteIndented = true };
    private static JsonDocumentOptions Lenient { get; } = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static McpServerFields? ToFields(NamedServer server)
    {
        JsonObject entry = server.Entry;
        if (!TryText(entry, CommandKey, out string? command)
            || !TryText(entry, UrlKey, out string? url)
            || !TryText(entry, TypeKey, out string? type))
            return null;

        McpServerType? kind = (command, url, type) switch
        {
            (not null, null, null or StdioType) => McpServerType.Command,
            (null, not null, null or HttpType) => McpServerType.Url,
            (null, null, null or StdioType) => McpServerType.Command,
            (null, null, HttpType) => McpServerType.Url,
            _ => null,
        };
        if (kind is not McpServerType chosen)
            return null;

        string[] typeKeys = chosen switch
        {
            McpServerType.Command => [CommandKey, ArgsKey, EnvKey, TypeKey],
            McpServerType.Url => [UrlKey, HeadersKey, TypeKey],
            _ => throw new ArgumentOutOfRangeException(nameof(chosen), chosen, null),
        };

        string arguments = string.Empty;
        string environment = string.Empty;
        string headers = string.Empty;
        if (chosen == McpServerType.Command
            && (!TryLines(entry[ArgsKey], out arguments) || !TryPairs(entry[EnvKey], "=", out environment)))
            return null;
        if (chosen == McpServerType.Url && !TryPairs(entry[HeadersKey], ": ", out headers))
            return null;

        JsonObject extra = [];
        foreach (KeyValuePair<string, JsonNode?> pair in entry)
        {
            if (!typeKeys.Contains(pair.Key))
                extra[pair.Key] = pair.Value?.DeepClone();
        }

        return new McpServerFields(server.Name, chosen, command ?? string.Empty, arguments, environment, url ?? string.Empty, headers, extra);
    }

    public static ServerResult<NamedServer> FromFields(McpServerFields fields)
    {
        string name = fields.Name.Trim();
        JsonObject entry = [];
        switch (fields.Type)
        {
            case McpServerType.Command:
                string command = fields.Command.Trim();
                if (command.Length == 0)
                    return new ServerResult<NamedServer>.Failed(new ServerProblem.CommandMissing(name));

                entry[CommandKey] = command;
                List<string> args = Lines(fields.Arguments).ToList();
                if (args.Count > 0)
                    entry[ArgsKey] = new JsonArray(args.Select(arg => (JsonNode?)JsonValue.Create(arg)).ToArray());
                switch (Pairs(fields.Environment, '=', McpServerField.Environment))
                {
                    case ServerResult<JsonObject>.Failed failed:
                        return new ServerResult<NamedServer>.Failed(failed.Problem);
                    case ServerResult<JsonObject>.Ok { Value.Count: > 0 } env:
                        entry[EnvKey] = env.Value;
                        break;
                }
                break;

            case McpServerType.Url:
                string url = fields.Url.Trim();
                if (HttpUrl(url) is null)
                    return new ServerResult<NamedServer>.Failed(new ServerProblem.UrlInvalid(name));

                entry[TypeKey] = HttpType;
                entry[UrlKey] = url;
                switch (Pairs(fields.Headers, ':', McpServerField.Headers))
                {
                    case ServerResult<JsonObject>.Failed failed:
                        return new ServerResult<NamedServer>.Failed(failed.Problem);
                    case ServerResult<JsonObject>.Ok { Value.Count: > 0 } headers:
                        entry[HeadersKey] = headers.Value;
                        break;
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(fields), fields.Type, null);
        }

        foreach (KeyValuePair<string, JsonNode?> pair in fields.Extra)
            entry[pair.Key] = pair.Value?.DeepClone();

        return new ServerResult<NamedServer>.Ok(new NamedServer(name, entry));
    }

    public static ServerResult<IReadOnlyList<NamedServer>> Read(string json)
    {
        try
        {
            return Servers(JsonNode.Parse(json, documentOptions: Lenient));
        }
        // A duplicate key throws ArgumentException when the JsonObject is first enumerated, after Parse returns.
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return new ServerResult<IReadOnlyList<NamedServer>>.Failed(new ServerProblem.JsonInvalid(ex.Message));
        }
    }

    private static ServerResult<IReadOnlyList<NamedServer>> Servers(JsonNode? root)
    {
        if (root is not JsonObject rootObject)
            return new ServerResult<IReadOnlyList<NamedServer>>.Failed(new ServerProblem.JsonNotObject());

        JsonObject named = rootObject[WrapperKey] is JsonObject wrapped ? wrapped : rootObject;
        if (named.ContainsKey(CommandKey) || named.ContainsKey(UrlKey))
            return new ServerResult<IReadOnlyList<NamedServer>>.Failed(new ServerProblem.Unnamed());
        if (named.Count == 0)
            return new ServerResult<IReadOnlyList<NamedServer>>.Failed(new ServerProblem.NoServers());

        List<NamedServer> servers = new(named.Count);
        foreach (KeyValuePair<string, JsonNode?> pair in named)
        {
            string name = pair.Key.Trim();
            if (pair.Value is not JsonObject entry)
                return new ServerResult<IReadOnlyList<NamedServer>>.Failed(new ServerProblem.NotAnObject(name));
            servers.Add(new NamedServer(name, (JsonObject)entry.DeepClone()));
        }

        return new ServerResult<IReadOnlyList<NamedServer>>.Ok(servers);
    }

    public static string Format(IReadOnlyList<NamedServer> servers)
    {
        JsonObject named = [];
        foreach (NamedServer server in servers)
            named[server.Name] = server.Entry.DeepClone();
        return named.ToJsonString(Indented);
    }

    public static ServerResult<IReadOnlyList<IMcpDefinition>> ToDefinitions(IReadOnlyList<NamedServer> servers, IReadOnlyCollection<string> taken, bool enabled)
    {
        HashSet<string> used = new(taken, StringComparer.OrdinalIgnoreCase);
        List<IMcpDefinition> definitions = new(servers.Count);
        foreach (NamedServer server in servers)
        {
            if (server.Name.Length == 0)
                return new ServerResult<IReadOnlyList<IMcpDefinition>>.Failed(new ServerProblem.NameMissing());
            if (!used.Add(server.Name))
                return new ServerResult<IReadOnlyList<IMcpDefinition>>.Failed(new ServerProblem.NameTaken(server.Name));

            switch (ToDefinition(server, enabled))
            {
                case ServerResult<IMcpDefinition>.Failed failed:
                    return new ServerResult<IReadOnlyList<IMcpDefinition>>.Failed(failed.Problem);

                case ServerResult<IMcpDefinition>.Ok ok when UnusedKeys(server, ok.Value) is { Count: > 0 } unused:
                    return new ServerResult<IReadOnlyList<IMcpDefinition>>.Failed(new ServerProblem.SettingsUnused(server.Name, unused));

                case ServerResult<IMcpDefinition>.Ok ok:
                    definitions.Add(ok.Value);
                    break;

                default:
                    throw new InvalidOperationException("Unknown server result.");
            }
        }

        return new ServerResult<IReadOnlyList<IMcpDefinition>>.Ok(definitions);
    }

    public static ServerResult<IMcpDefinition> ToDefinition(NamedServer server, bool enabled)
    {
        if (!Tool.IsCanonicalName(server.Name))
            return new ServerResult<IMcpDefinition>.Failed(new ServerProblem.NameInvalid(server.Name));
        if (!TryText(server.Entry, TypeKey, out string? type))
            return new ServerResult<IMcpDefinition>.Failed(new ServerProblem.ValueInvalid(server.Name, TypeKey));
        if (type is not (null or StdioType or HttpType))
            return new ServerResult<IMcpDefinition>.Failed(new ServerProblem.TypeUnsupported(server.Name, type));

        bool isUrl = type == HttpType || (type is null && server.Entry.ContainsKey(UrlKey));
        return isUrl ? ToHttp(server, enabled) : ToStdio(server, enabled);
    }

    public static IReadOnlyList<string> UnusedKeys(NamedServer server, IMcpDefinition definition)
    {
        string[] used = definition switch
        {
            StdioMcpDefinition => [TypeKey, CommandKey, ArgsKey, EnvKey],
            HttpMcpDefinition => [TypeKey, UrlKey, HeadersKey],
            _ => throw new ArgumentOutOfRangeException(nameof(definition), definition, "Unknown MCP definition."),
        };

        return server.Entry.Select(pair => pair.Key).Where(key => !used.Contains(key)).ToList();
    }

    public static NamedServer ToServer(IMcpDefinition definition) => definition switch
    {
        StdioMcpDefinition stdio => new NamedServer(stdio.Name, StdioEntry(stdio)),
        HttpMcpDefinition http => new NamedServer(http.Name, HttpEntry(http)),
        _ => throw new ArgumentOutOfRangeException(nameof(definition), definition, "Unknown MCP definition."),
    };

    private static ServerResult<IMcpDefinition> ToStdio(NamedServer server, bool enabled)
    {
        JsonObject entry = server.Entry;
        if (!TryText(entry, CommandKey, out string? command))
            return new ServerResult<IMcpDefinition>.Failed(new ServerProblem.ValueInvalid(server.Name, CommandKey));
        if (string.IsNullOrWhiteSpace(command))
            return new ServerResult<IMcpDefinition>.Failed(new ServerProblem.CommandMissing(server.Name));
        if (!TryStrings(entry[ArgsKey], out IReadOnlyList<string> args))
            return new ServerResult<IMcpDefinition>.Failed(new ServerProblem.ValueInvalid(server.Name, ArgsKey));
        if (!TryStringMap(entry[EnvKey], out IReadOnlyDictionary<string, string> env))
            return new ServerResult<IMcpDefinition>.Failed(new ServerProblem.ValueInvalid(server.Name, EnvKey));

        return new ServerResult<IMcpDefinition>.Ok(new StdioMcpDefinition(server.Name, enabled, command.Trim(), args, env));
    }

    private static ServerResult<IMcpDefinition> ToHttp(NamedServer server, bool enabled)
    {
        JsonObject entry = server.Entry;
        if (!TryText(entry, UrlKey, out string? url) || HttpUrl(url) is not Uri address)
            return new ServerResult<IMcpDefinition>.Failed(new ServerProblem.UrlInvalid(server.Name));
        if (!TryStringMap(entry[HeadersKey], out IReadOnlyDictionary<string, string> headers))
            return new ServerResult<IMcpDefinition>.Failed(new ServerProblem.ValueInvalid(server.Name, HeadersKey));

        return new ServerResult<IMcpDefinition>.Ok(new HttpMcpDefinition(server.Name, enabled, address, headers));
    }

    private static JsonObject StdioEntry(StdioMcpDefinition stdio)
    {
        JsonObject entry = new() { [CommandKey] = stdio.Command };
        if (stdio.Args.Count > 0)
            entry[ArgsKey] = new JsonArray(stdio.Args.Select(arg => (JsonNode?)JsonValue.Create(arg)).ToArray());
        if (stdio.Env.Count > 0)
            entry[EnvKey] = ToObject(stdio.Env);
        return entry;
    }

    private static JsonObject HttpEntry(HttpMcpDefinition http)
    {
        JsonObject entry = new() { [TypeKey] = HttpType, [UrlKey] = http.Url.OriginalString };
        if (http.Headers.Count > 0)
            entry[HeadersKey] = ToObject(http.Headers);
        return entry;
    }

    private static JsonObject ToObject(IReadOnlyDictionary<string, string> values)
    {
        JsonObject pairs = [];
        foreach (KeyValuePair<string, string> pair in values)
            pairs[pair.Key] = pair.Value;
        return pairs;
    }

    private static Uri? HttpUrl(string? text)
        => Uri.TryCreate(text?.Trim(), UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    private static bool TryText(JsonObject entry, string key, out string? text)
    {
        text = null;
        if (!entry.TryGetPropertyValue(key, out JsonNode? node) || node is null)
            return true;
        if (node.GetValueKind() != JsonValueKind.String)
            return false;

        text = node.GetValue<string>();
        return true;
    }

    private static bool TryLines(JsonNode? node, out string text)
    {
        bool read = TryStrings(node, out IReadOnlyList<string> values);
        text = string.Join("\n", values);
        return read;
    }

    private static bool TryPairs(JsonNode? node, string separator, out string text)
    {
        bool read = TryStringMap(node, out IReadOnlyDictionary<string, string> values);
        text = string.Join("\n", values.Select(pair => pair.Key + separator + pair.Value));
        return read;
    }

    private static bool TryStrings(JsonNode? node, out IReadOnlyList<string> values)
    {
        values = [];
        if (node is null)
            return true;
        if (node is not JsonArray array || array.Any(item => item?.GetValueKind() != JsonValueKind.String))
            return false;

        values = array.Select(item => item!.GetValue<string>()).ToList();
        return true;
    }

    private static bool TryStringMap(JsonNode? node, out IReadOnlyDictionary<string, string> values)
    {
        values = new Dictionary<string, string>();
        if (node is null)
            return true;
        if (node is not JsonObject pairs || pairs.Any(pair => pair.Value?.GetValueKind() != JsonValueKind.String))
            return false;

        values = pairs.ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>());
        return true;
    }

    private static IEnumerable<string> Lines(string text)
        => text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);

    private static ServerResult<JsonObject> Pairs(string text, char separator, McpServerField field)
    {
        JsonObject pairs = [];
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0)
                continue;

            int split = line.IndexOf(separator);
            string key = split < 0 ? string.Empty : line.Substring(0, split).Trim();
            if (key.Length == 0)
                return new ServerResult<JsonObject>.Failed(new ServerProblem.BadLine(field, index + 1));

            pairs[key] = line.Substring(split + 1).Trim();
        }

        return new ServerResult<JsonObject>.Ok(pairs);
    }
}
