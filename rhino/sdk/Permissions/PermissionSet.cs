using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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

    [JsonInclude]
    private HashSet<Permission> Permissions { get; set; } = new();

    [JsonIgnore]
    public IReadOnlyCollection<Permission> All => Permissions;

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
        foreach (Permission permission in Naming(mcpName, toolName))
        {
            // TODO : What if there are multiple entries?
            result = ResolvePermissions(permission, args);
        }

        return result;
    }

    private IEnumerable<Permission> Naming(string mcpName, string toolName)
    {
        foreach (Permission permission in Permissions)
        {
            if (!string.Equals(permission.McpName, mcpName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(permission.McpName, "*")) continue;

            if (!string.Equals(permission.ToolName, toolName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(permission.ToolName, "*")) continue;

            yield return permission;
        }
    }

    internal static Permissability ResolvePermissionChain(IAgentDefinition? definition, Mcp mcp, ITool tool, IList<IToolArg> args)
        => ResolvePermissionChain(definition, mcp.Name, tool.Name, args);

    internal static Permissability ResolvePermissionChain(IAgentDefinition? definition, string mcpName, string toolName, IList<IToolArg> args)
    {
        if (definition is null) return Permissability.Deny;

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

    internal IReadOnlyList<string> ToolsDeniedForAnyArguments(string mcpName)
    {
        List<string> denied = [];
        foreach (Permission permission in ProhibitedTools())
        {
            if (!string.Equals(permission.McpName, mcpName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(permission.McpName, "*")) continue;

            if (!AcceptsAnyArguments(permission)) continue;

            denied.Add(permission.ToolName);
        }

        return denied;
    }

    // Only a tool whose every rule is an unconditional Always qualifies, so no argument can change the chain's answer.
    internal static IReadOnlyList<string> ToolsAllowedForAnyArguments(IAgentDefinition? definition, string mcpName)
    {
        if (definition is null) return [];

        PermissionSet global = AIInstances.Preferences.Permissions;
        HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase);
        foreach (Permission candidate in global.Permissions.Concat(definition.Permissions.Permissions))
        {
            if (candidate.Permissability != Permissability.Always || candidate.ToolName == "*") continue;
            if (!string.Equals(candidate.McpName, mcpName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!global.IsAlwaysForAnyArguments(mcpName, candidate.ToolName) || !definition.Permissions.IsAlwaysForAnyArguments(mcpName, candidate.ToolName)) continue;
            if (ResolvePermissionChain(definition, mcpName, candidate.ToolName, []) != Permissability.Always) continue;

            allowed.Add(candidate.ToolName);
        }

        return [.. allowed];
    }

    private bool IsAlwaysForAnyArguments(string mcpName, string toolName)
    {
        foreach (Permission permission in Naming(mcpName, toolName))
        {
            if (permission.Permissability != Permissability.Always || !AcceptsAnyArguments(permission))
                return false;
        }

        return true;
    }

    private static bool AcceptsAnyArguments(Permission permission)
    {
        foreach (ToolArgPermission arg in permission.ArgumentPermissions.Values)
        {
            if (arg.Value.Trim() != ArgRule.Anything)
                return false;
        }

        return true;
    }

    private Permissability ResolvePermissions(Permission permission, IList<IToolArg> args)
    {
        foreach (IToolArg inputArg in args)
        {
            if (!permission.ArgumentPermissions.TryGetValue(inputArg.Name, out ToolArgPermission argPermission))
                return DefaultPermission;
            if (!Matches(argPermission, inputArg))
                return DefaultPermission;
        }

        foreach (KeyValuePair<string, ToolArgPermission> rule in permission.ArgumentPermissions)
        {
            if (!IsSupplied(args, rule.Key) && rule.Value.Value.Trim() != ArgRule.Anything)
                return DefaultPermission;
        }

        return permission.Permissability;
    }

    private static bool IsSupplied(IList<IToolArg> args, string name)
    {
        foreach (IToolArg arg in args)
        {
            if (string.Equals(arg.Name, name))
                return true;
        }

        return false;
    }

    internal bool AddPermission(Permission permission)
        => Permissions.Add(permission);

    internal void Replace(Permission permission)
    {
        Permissions.RemoveWhere(existing =>
            string.Equals(existing.McpName, permission.McpName) &&
            string.Equals(existing.ToolName, permission.ToolName));
        Permissions.Add(permission);
    }

    internal void Clear() => Permissions.Clear();


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
        ToolJson json => rule.MatchesText(json.Value.GetRawText()),

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

    [JsonConstructor]
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
        : this(mcpName, toolName, permissability, (IReadOnlyDictionary<string, ToolArgPermission>)argumentPermissions)
    {
    }

    [JsonConstructor]
    private Permission(string mcpName, string toolName, Permissability permissability, IReadOnlyDictionary<string, ToolArgPermission> argumentPermissions)
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
