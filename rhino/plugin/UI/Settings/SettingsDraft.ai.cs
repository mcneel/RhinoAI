using Rhino.AI.Models;

namespace Rhino.AI.UI.Settings;

internal abstract record KeyChange;

internal sealed record KeepKey : KeyChange;
internal sealed record SetKey(string Value) : KeyChange;
internal sealed record ClearKey : KeyChange;

internal abstract record ConnectionDraft;

internal sealed record CliConnectionDraft(IReadOnlyList<SearchPath> WindowsPaths, IReadOnlyList<SearchPath> MacPaths) : ConnectionDraft;
internal sealed record ApiConnectionDraft(string Host, ProtocolKind Protocol, string? KeyName, KeyChange Key) : ConnectionDraft;
internal sealed record LocalConnectionDraft(string Host, ProtocolKind Protocol, string ModelsRoute) : ConnectionDraft;

internal sealed record AgentDraft(
    string Id,
    string Name,
    string Vendor,
    bool Enabled,
    string? DefaultModelId,
    IReadOnlyList<ModelEntry> Models,
    string Prompt,
    ConnectionDraft Connection,
    IReadOnlyList<HostOs> Platforms);

internal sealed record SettingsProblem(string? Target, string? Field, string Message);
