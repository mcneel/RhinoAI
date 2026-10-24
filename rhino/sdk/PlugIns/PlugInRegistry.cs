using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Rhino.AI.PlugIns;

/// <summary>
/// Registry for PlugIn Tokens. Required for permissions.
/// </summary>
public static class PlugInRegistry
{

    private sealed record PlugInPermission(bool Allowed);

    private static Dictionary<PlugInToken, PlugInPermission> Permissions { get; } = [];
    private static Dictionary<Assembly, PlugInToken> Tokens { get; } = [];

    /// <summary>
    /// Register a PlugIn in the permissions system. This method will always return the same token instance to the assembly that calls it.
    /// </summary>
    /// <returns>A Token for the requesting Assembly</returns>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization | MethodImplOptions.Synchronized)]
    public static PlugInToken? Register()
    {
        try
        {
            Assembly assembly = Assembly.GetCallingAssembly();
            if (Tokens.TryGetValue(assembly, out PlugInToken? token)) return token;

            GuidAttribute guidAttribute = assembly.GetCustomAttribute<GuidAttribute>()
                ?? throw new InvalidOperationException($"{assembly.FullName} has no [assembly: Guid] attribute");

            Guid id = Guid.Parse(guidAttribute.Value);
            if (id == Guid.Empty) return null;

            string name = assembly.GetName()?.Name ?? assembly.FullName ?? id.ToString();
            token = new(id, name);

            Tokens[assembly] = token;
            Permissions[token] = new PlugInPermission(false);

            return token;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Checks a <see cref="PlugInToken"/> for user granted permissions
    /// </summary>
    /// <param name="token">A permissions token for a PlugIn</param>
    /// <returns>True if the user has granted permission for the PlugIn</returns>
    public static bool HasPermission(PlugInToken token)
    {
        if (ByPass) return true;
        if (!Permissions.TryGetValue(token, out PlugInPermission? permission)) return false;
        if (permission is null) return false;
        return permission.Allowed;
    }

    private static Dictionary<PlugInToken, List<ISkill>> PrivatePanelSkills { get; } = [];
    public static IReadOnlyCollection<ISkill> PanelSkills => PrivatePanelSkills.SelectMany(pps => pps.Value).ToArray();

    /// <summary>
    /// Registers a skill with the AI Panel
    /// </summary>
    /// <param name="token">A permissions token for a PlugIn</param>
    /// <param name="skill">The skill to register</param>
    /// <returns>true on success, false otherwise</returns>
    public static bool RegisterPanelSkill(PlugInToken token, ISkill skill)
    {
        PrivatePanelSkills.TryGetValue(token, out List<ISkill>? skills);
        skills ??= new();

        if (skills.Any(s => IsEqual(s, skill))) return false;

        skills.Add(skill);
        PrivatePanelSkills[token] = skills;
        return true;
    }

    private static bool IsEqual(ISkill a, ISkill b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (!string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(a.Description, b.Description, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static Dictionary<PlugInToken, List<IMcp>> PrivatePanelMcps { get; } = [];
    public static IReadOnlyCollection<IMcp> PanelMcps => PrivatePanelMcps.SelectMany(pps => pps.Value).ToArray();

    /// <summary>
    /// Registers an MCP with the AI Panel
    /// </summary>
    /// <param name="token">A permissions token for a PlugIn</param>
    /// <param name="mcp">The mcp to register. Do not use this same instance with other Harnesses or Agents</param>
    /// <returns>true on success, false otherwise</returns>
    public static bool RegisterPanelMcp(PlugInToken token, IMcp mcp)
    {
        PrivatePanelMcps.TryGetValue(token, out List<IMcp>? Mcps);
        Mcps ??= new();

        if (Mcps.Any(s => IsEqual(s, mcp))) return false;

        Mcps.Add(mcp);
        PrivatePanelMcps[token] = Mcps;
        return true;
    }

    private static bool IsEqual(IMcp a, IMcp b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (!string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (a.Tools.Count != b.Tools.Count) return false;

        string[] aNames = a.Tools.Keys.ToArray();
        string[] bNames = b.Tools.Keys.ToArray();

        for (int i = 0; i < a.Tools.Count; i++)
        {
            if (!string.Equals(aNames[i], bNames[i], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private const string BYPASS_ENV_VAR = "BYPASS_AI_PERMISSIONS";
    private static bool ByPass { get; } = false;

    static PlugInRegistry()
    {
        try
        {
            string? value = Environment.GetEnvironmentVariable(BYPASS_ENV_VAR);
            ByPass = bool.TryParse(value, out bool result) && result;
        }
        catch { }
    }

}
