using System;
using System.Collections.Generic;

namespace Rhino.AI;

public class PermissionSet
{

    public Permissability DefaultPermission { get; set; } = Permissability.Ask;

    private Dictionary<string, Permission> Permissions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Permissability HasPermission(ITool tool, IReadOnlyList<IToolArg> args)
    {
        Permissability result = DefaultPermission;
        if (Permissions.TryGetValue(tool.Name, out Permission? permission))
        {
            if (permission is not null)
            {
                result = ResolvePermissions(permission, args);
            }
            else
            {
                Permissions.Remove(tool.Name);
            }
        }

        return result;
    }
    
    public IEnumerable<Permission> AllowedTools()
    {
        foreach(KeyValuePair<string, Permission> permission in Permissions)
        {
            if (permission.Value.Permissability != Permissability.Always) continue;
            yield return permission.Value;
        }
    }
    
    public IEnumerable<Permission> ProhibitedTools()
    {
        foreach(KeyValuePair<string, Permission> permission in Permissions)
        {
            if (permission.Value.Permissability != Permissability.Deny) continue;
            yield return permission.Value;
        }
    }

    private Permissability ResolvePermissions(Permission permission, IReadOnlyList<IToolArg> args)
    {
        foreach (IToolArg inputArg in args)
        {
            if (permission.ArgumentPermissions.TryGetValue(inputArg.Name, out ToolArgPermission argPermission))
            {
                // No Permission saved
                if (!Matches(argPermission, inputArg)) return DefaultPermission;
            }
            else
            {
                // We don't have a record of this arg
                return DefaultPermission;
            }
        }

        // All args passed the sniff test
        return permission.Permissability;
    }

    public bool AddPermission(string name, Permission permission)
        => Permissions.TryAdd(name, permission);
    

    // All rules can amtch * as ANYTHING
    // OR !! as NOTHING
    private static bool Matches(ToolArgPermission argPermission, IToolArg value) => argPermission.Value.Trim() switch
    {
        ArgRule.Anything => true,
        ArgRule.Nothing => false,
        string rule => MatchesRule(ArgRule.Parse(rule), value),
    };

    private static bool MatchesRule(ArgRule rule, IToolArg value) => value switch
    {
        IToolBoolean boolean => rule.MatchesBoolean(boolean.Value),
        IToolInt integer => rule.MatchesNumber(integer.Value),
        IToolNumber number => rule.MatchesNumber(number.Value),
        IToolPath path => rule.MatchesPath(path.Value),
        IToolUrl url => rule.MatchesUrl(url.Value),
        IToolString text => rule.MatchesText(text.Value),

        _ => false
    };

}

public enum Permissability { Always, Ask, Deny };

public record struct ToolArgPermission(string ArgName, Permissability Permissability, string Value);

public record Permission(string ToolName, Permissability Permissability, Dictionary<string, ToolArgPermission> ArgumentPermissions);
