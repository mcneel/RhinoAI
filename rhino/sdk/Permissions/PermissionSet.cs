using System;
using System.Collections.Generic;

namespace Rhino.AI;

/// <summary>
/// A set of permissions
/// </summary>
public sealed class PermissionSet
{

    /// <summary>
    /// The default permission used when no prior permission has been requested
    /// </summary>
    public Permissability DefaultPermission { get; set; } = Permissability.Unset;

    private HashSet<Permission> Permissions { get; } = new();

    /// <summary>
    /// What permission does this mcp/tool with the given args have?
    /// </summary>
    /// <param name="mcp">The MCP</param>
    /// <param name="tool">A Tool of this MCP</param>
    /// <param name="args">The arguments</param>
    /// <returns>The given permission, or the default if none is found</returns>
    public Permissability HasPermission(Mcp mcp, ITool tool, IList<IToolArg> args)
        => HasPermission(mcp.Name, tool.Name, args);

    /// <summary>
    /// What permission does this mcp/tool with the given args have?
    /// </summary>
    /// <param name="mcpName">The name of the MCP</param>
    /// <param name="toolName">The name of the Tool</param>
    /// <param name="args">The arguments</param>
    /// <returns>The given permission, or the default if none is found</returns>
    public Permissability HasPermission(string mcpName, string toolName, IList<IToolArg> args)
    {
        Permissability result = DefaultPermission;
        foreach (Permission permission in Permissions)
        {
            if (!string.Equals(permission.McpName, mcpName) &&
                !string.Equals(permission.McpName, "*")) continue;

            if (!string.Equals(permission.ToolName, toolName) &&
                !string.Equals(permission.ToolName, "*")) continue;

            // TODO : What if there are multiple entries?
            result = ResolvePermissions(permission, args);
        }

        return result;
    }

    internal static Permissability ResolvePermissionChain(IAgentDefinition definition, Mcp mcp, ITool tool, IList<IToolArg> args)
        => ResolvePermissionChain(definition, mcp.Name, tool.Name, args);

    internal static Permissability ResolvePermissionChain(IAgentDefinition definition, string mcpName, string toolName, IList<IToolArg> args)
    {
        Permissability globalPermissions = AIInstances.Preferences.Permissions.HasPermission(mcpName, toolName, args);
        if (globalPermissions == Permissability.Deny) return Permissability.Deny;

        Permissability defPermissions = definition.Permissions.HasPermission(mcpName, toolName, args);
        if (defPermissions == Permissability.Deny) return Permissability.Deny;
        
        // Never specified
        if (globalPermissions == Permissability.Unset && defPermissions == Permissability.Unset) return AIInstances.Preferences.DefaultPermission;

        if (globalPermissions == Permissability.Always && defPermissions == Permissability.Unset) return globalPermissions;
        if (globalPermissions == Permissability.Unset && defPermissions == Permissability.Always) return defPermissions;
        
        // TODO : Add a test -- I think some scenarios are missing here
        
        return globalPermissions;
    }
    
    /// <summary>
    /// All tools that are Permissability.Always
    /// </summary>
    public IEnumerable<Permission> AllowedTools()
    {
        foreach (Permission permission in Permissions)
        {
            if (permission.Permissability != Permissability.Always) continue;
            yield return permission;
        }
    }

    /// <summary>
    /// All tools that are Permissability.Deny
    /// </summary>
    public IEnumerable<Permission> ProhibitedTools()
    {
        foreach (Permission permission in Permissions)
        {
            if (permission.Permissability != Permissability.Deny) continue;
            yield return permission;
        }
    }

    private Permissability ResolvePermissions(Permission permission, IList<IToolArg> args)
    {
        if (permission.ArgumentPermissions.Count != args.Count)
            return DefaultPermission;

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

    internal bool AddPermission(Permission permission)
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
        ToolSecret secret => rule.MatchesText(secret.Value),

        _ => false
    };

}

/// <summary>
/// The current permission of a tool
/// </summary>
public enum Permissability { Unset = 0, Always, Ask, Deny };

/// <summary>
/// A given tool argument permission
/// </summary>
public readonly record struct ToolArgPermission
{

    public string ArgName { get; }
    public string Value { get; }

    public ToolArgPermission(string argName, string value)
    {
        ArgName = argName;
        Value = value;
    }

}

/// <summary>
/// A single permission for a specific tool
/// </summary>
public sealed record Permission
{

    /// <summary>
    /// The name of the MCP the tool belongs to
    /// </summary>
    public string McpName { get; }

    /// <summary>
    /// The name of the Tool
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// The users specified permission
    /// </summary>
    public Permissability Permissability { get; }

    /// <summary>
    /// The permission argumnets
    /// </summary>
    public IReadOnlyDictionary<string, ToolArgPermission> ArgumentPermissions { get; }

    internal Permission(string mcpName, string toolName, Permissability permissability, Dictionary<string, ToolArgPermission> argumentPermissions)
    {
        McpName = mcpName;
        ToolName = toolName;
        Permissability = permissability;
        ArgumentPermissions = argumentPermissions;
    }
    
    public Permission(Mcp mcp, ITool tool, Permissability permissability, Dictionary<string, ToolArgPermission> argumentPermissions)
    {
        McpName = mcp.Name;
        ToolName = tool.Name;
        Permissability = permissability;
        ArgumentPermissions = argumentPermissions;
    }

    public Permission(Mcp mcp, Permissability permissability, Dictionary<string, ToolArgPermission> argumentPermissions)
    {
        McpName = mcp.Name;
        ToolName = "*";
        Permissability = permissability;
        ArgumentPermissions = argumentPermissions;
    }

}
