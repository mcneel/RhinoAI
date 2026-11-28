namespace Rhino.AI.UI.Settings;

internal static class PermissionRules
{
    public static PermissionsSettings Current()
    {
        UserPreferences preferences = AIInstances.Preferences;
        List<ToolRule> rules = [];
        foreach (Permission permission in preferences.Permissions.All)
        {
            if (ModeOf(permission.Permissability) is not ToolMode mode)
                continue;

            List<ArgCondition> args = permission.ArgumentPermissions
                .Select(entry => new ArgCondition(entry.Key, entry.Value.Value))
                .ToList();
            rules.Add(new ToolRule(permission.McpName, permission.ToolName, mode, args));
        }

        // An unset default lets every call through, since Mcp.RunToolAsync only stops Deny and asks for Ask.
        return new PermissionsSettings(ModeOf(preferences.DefaultPermission) ?? ToolMode.Allow, rules);
    }

    public static void Apply(PermissionsSettings permissions)
    {
        UserPreferences preferences = AIInstances.Preferences;
        preferences.DefaultPermission = ToPermissability(permissions.DefaultMode);
        preferences.Permissions.Clear();
        foreach (ToolRule rule in permissions.Rules)
        {
            Dictionary<string, ToolArgPermission> args = new(rule.Args.Count);
            foreach (ArgCondition condition in rule.Args)
                args[condition.Name] = new ToolArgPermission(condition.Name, condition.Rule);

            preferences.Permissions.AddPermission(new Permission(rule.Server, rule.Tool, ToPermissability(rule.Mode), args));
        }
    }

    private static ToolMode? ModeOf(Permissability permissability) => permissability switch
    {
        Permissability.Unset => null,
        Permissability.Always => ToolMode.Allow,
        Permissability.Ask => ToolMode.Ask,
        Permissability.Deny => ToolMode.Deny,
        _ => throw new ArgumentOutOfRangeException(nameof(permissability), permissability, null),
    };

    private static Permissability ToPermissability(ToolMode mode) => mode switch
    {
        ToolMode.Allow => Permissability.Always,
        ToolMode.Ask => Permissability.Ask,
        ToolMode.Deny => Permissability.Deny,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
