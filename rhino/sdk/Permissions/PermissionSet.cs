using System;
using System.Collections.Generic;

namespace Rhino.AI;

/// <summary>
/// A set of permissions
/// </summary>
public sealed class PermissionSet
{

    public Permissability DefaultPermission { get; set; } = Permissability.Ask;

    private HashSet<Permission> Permissions { get; } = new();

    public Permissability HasPermission(IMcp mcp, ITool tool, IReadOnlyList<IToolArg> args)
        => HasPermission(mcp.Name, tool.Name, args);

    public Permissability HasPermission(string mcpName, string toolName, IReadOnlyList<IToolArg> args)
    {
        Permissability result = DefaultPermission;
        foreach(Permission permission in Permissions)
        {
            if (!string.Equals(permission.McpName, mcpName)) continue;
            if (!string.Equals(permission.ToolName, toolName)) continue;

            // TODO : What if there are multiple entries
            result = ResolvePermissions(permission, args);
        }

        return result;
    }

    public IEnumerable<Permission> AllowedTools()
    {
        foreach (Permission permission in Permissions)
        {
            if (permission.Permissability != Permissability.Always) continue;
            yield return permission;
        }
    }

    public IEnumerable<Permission> ProhibitedTools()
    {
        foreach (Permission permission in Permissions)
        {
            if (permission.Permissability != Permissability.Deny) continue;
            yield return permission;
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

    public bool AddPermission(Permission permission)
        => Permissions.Add(permission);


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
        ToolBoolean boolean => rule.MatchesBoolean(boolean.Value),
        ToolInt integer => rule.MatchesNumber(integer.Value),
        ToolNumber number => rule.MatchesNumber(number.Value),
        ToolPath path => rule.MatchesPath(path.Value),
        ToolUrl url => rule.MatchesUrl(url.Value),
        ToolString text => rule.MatchesText(text.Value),

        _ => false
    };

}

public enum Permissability { Always, Ask, Deny };

public record struct ToolArgPermission(string ArgName, Permissability Permissability, string Value);

public sealed record Permission
{

    public string McpName { get; }

    public string ToolName { get; }

    public Permissability Permissability { get; }

    public IReadOnlyDictionary<string, ToolArgPermission> ArgumentPermissions { get; }

    public Permission(IMcp mcp, ITool tool, Permissability permissability, Dictionary<string, ToolArgPermission> argumentPermissions)
    {
        McpName = mcp.Name;
        ToolName = tool.Name;
        Permissability = permissability;
        ArgumentPermissions = argumentPermissions;
    }

    public Permission(IMcp mcp, Permissability permissability, Dictionary<string, ToolArgPermission> argumentPermissions)
    {
        McpName = mcp.Name;
        ToolName = "*";
        Permissability = permissability;
        ArgumentPermissions = argumentPermissions;
    }

}
