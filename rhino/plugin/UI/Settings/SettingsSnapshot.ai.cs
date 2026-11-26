#if R9
using Rhino.AI.Models;

namespace Rhino.AI.UI.Settings;

internal enum HostOs { Windows, Mac }

internal enum KeySource { None, Environment, Vault }

internal enum AgentKind { Cli, Api, Local }

internal enum ToolMode { Allow, Ask, Deny }

internal enum ToolEffect { Read, Edit, Destructive }

internal enum McpTransport { Http, Stdio, InProcess }

internal sealed record ModelEntry(string Id, string Name);

internal abstract record AgentConnection;

internal sealed record CliConnection(IReadOnlyList<SearchPath> WindowsPaths, IReadOnlyList<SearchPath> MacPaths) : AgentConnection;
internal sealed record ApiConnection(string Host, ProtocolKind Protocol, string? KeyName, KeySource KeySource) : AgentConnection;
internal sealed record LocalConnection(string Host, ProtocolKind Protocol, string ModelsRoute) : AgentConnection;

internal sealed record AgentSettings(
    string Id,
    string Name,
    string Vendor,
    bool Enabled,
    string? DefaultModelId,
    IReadOnlyList<ModelEntry> Models,
    string Prompt,
    AgentConnection Connection,
    bool Builtin,
    IReadOnlyList<HostOs> Platforms);

internal sealed record ArgCondition(string Name, string Rule);
internal sealed record ToolRule(string Server, string Tool, ToolMode Mode, IReadOnlyList<ArgCondition> Args);
internal sealed record PermissionsSettings(ToolMode DefaultMode, IReadOnlyList<ToolRule> Rules);

internal sealed record ToolInfo(string Name, string Title, string Description, ToolEffect Effect, IReadOnlyList<string> Args);
internal sealed record ServerInfo(string Name, string Title, McpTransport Transport, IReadOnlyList<ToolInfo> Tools);
internal sealed record SkillInfo(string Name, string Description);
internal sealed record PlugInInfo(string Id, string Name, bool Trusted, bool Loaded, string? Icon, IReadOnlyList<ServerInfo> Servers, IReadOnlyList<SkillInfo> Skills);
internal sealed record ToolCatalog(ServerInfo Builtin, ServerInfo DefaultTools, IReadOnlyList<PlugInInfo> Plugins);

internal sealed record UsageTurn(DateTimeOffset At, string AgentId, string AgentName, int InputTokens, int OutputTokens, decimal? CostUsd);
#endif
