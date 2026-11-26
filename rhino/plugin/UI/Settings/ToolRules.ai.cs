#if R9
namespace Rhino.AI.UI.Settings;

internal sealed record ToolChange(string Server, string Tool, Permissability Permission, IReadOnlyList<string> Args);

// PermissionSet only applies a rule when it names every argument of the call, and lets the last matching rule win.
internal static class ToolRules
{
    private const string Any = "*";

    public static bool IsPlain(Permission rule)
        => rule.ArgumentPermissions.Values.All(arg => arg.Value.Trim() == ArgRule.Anything);

    public static Permissability Explicit(IReadOnlyList<Permission> rules, string server, string tool)
        => rules.LastOrDefault(rule => rule.McpName == server && rule.ToolName == tool && IsPlain(rule))?.Permissability
            ?? Permissability.Unset;

    public static Permissability ServerWide(IReadOnlyList<Permission> rules, string server)
        => rules.LastOrDefault(rule => (rule.McpName == server || rule.McpName == Any) && rule.ToolName == Any && IsPlain(rule))?.Permissability
            ?? Permissability.Unset;

    public static bool HasConditions(IReadOnlyList<Permission> rules, string server, string tool)
        => rules.Any(rule => rule.McpName == server && rule.ToolName == tool && !IsPlain(rule));

    public static IReadOnlyList<Permission> WithChanges(IReadOnlyList<Permission> rules, IReadOnlyList<ToolChange> changes)
    {
        Dictionary<(string Server, string Tool), ToolChange> pending = new(changes.Count);
        foreach (ToolChange change in changes)
            pending[(change.Server, change.Tool)] = change;

        Dictionary<(string Server, string Tool), int> lastPlain = [];
        for (int index = 0; index < rules.Count; index++)
        {
            Permission rule = rules[index];
            if (IsPlain(rule) && pending.ContainsKey((rule.McpName, rule.ToolName)))
                lastPlain[(rule.McpName, rule.ToolName)] = index;
        }

        List<Permission> result = new(rules.Count + changes.Count);
        for (int index = 0; index < rules.Count; index++)
        {
            Permission rule = rules[index];
            (string Server, string Tool) key = (rule.McpName, rule.ToolName);
            if (!IsPlain(rule) || !pending.TryGetValue(key, out ToolChange? change))
            {
                result.Add(rule);
                continue;
            }

            if (lastPlain[key] == index && Rule(change, rule) is Permission replacement)
                result.Add(replacement);
        }

        foreach (ToolChange change in pending.Values)
        {
            if (!lastPlain.ContainsKey((change.Server, change.Tool)) && Rule(change, null) is Permission added)
                result.Add(added);
        }

        return result;
    }

    private static Permission? Rule(ToolChange change, Permission? previous)
    {
        if (change.Permission == Permissability.Unset)
            return null;

        Dictionary<string, ToolArgPermission> args = new(StringComparer.Ordinal);
        foreach (string name in (previous?.ArgumentPermissions.Keys ?? []).Concat(change.Args))
            args[name] = new ToolArgPermission(name, ArgRule.Anything);

        return new Permission(change.Server, change.Tool, change.Permission, args);
    }
}
#endif
