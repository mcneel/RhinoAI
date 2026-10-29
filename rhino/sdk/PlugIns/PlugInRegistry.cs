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
    // TODO : Instances!

    private sealed record PlugInPermission(bool Allowed);

    private static Dictionary<PlugInToken, PlugInPermission> Permissions { get; } = [];
    private static Dictionary<Assembly, PlugInToken> Tokens { get; } = [];

    private static Dictionary<PlugInToken, AgenticPlugIn> PrivatePlugIns { get; } = [];
    internal static IReadOnlyDictionary<PlugInToken, AgenticPlugIn> PlugIns => PrivatePlugIns;

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

            bool permission = false;
            // Default to true for Script Editor as this is users running their own scripts
            if (id == new Guid("c9cba87a-23ce-4f15-a918-97645c05cde7"))
            {
                permission = true;
            }

            Tokens[assembly] = token;
            Permissions[token] = new PlugInPermission(permission);

            if (AgenticPlugIn.Get(assembly, id, name) is { } plugin)
            {
                PrivatePlugIns.Add(token, plugin);
            }

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

    internal static void GivePermission(PlugInToken token)
    {
        Permissions[token] = new PlugInPermission(true);
    }

    internal static Dictionary<PlugInToken, List<ISkill>> PrivatePanelSkills { get; } = [];
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

    internal static Dictionary<PlugInToken, List<Mcp>> PrivatePanelMcps { get; } = [];
    public static IReadOnlyCollection<Mcp> PanelMcps => PrivatePanelMcps.SelectMany(pps => pps.Value).ToArray();

    /// <summary>
    /// Registers an MCP with the AI Panel
    /// </summary>
    /// <param name="token">A permissions token for a PlugIn</param>
    /// <param name="mcp">The mcp to register. Do not use this same instance with other Harnesses or Agents</param>
    /// <returns>true on success, false otherwise</returns>
    public static bool RegisterPanelMcp(PlugInToken token, Mcp mcp)
    {
        PrivatePanelMcps.TryGetValue(token, out List<Mcp>? Mcps);
        Mcps ??= new();

        if (Mcps.Any(s => IsEqual(s, mcp))) return false;

        Mcps.Add(mcp);
        PrivatePanelMcps[token] = Mcps;
        return true;
    }

    private static bool IsEqual(Mcp a, Mcp b)
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

    public class AgenticPlugIn
    {
        public Guid Id { get; }

        public string Name { get; }

        public string IconResource { get; }

        public Assembly Assembly { get; }

        private AgenticPlugIn(Assembly assembly, Guid id, string name, string iconResource)
        {
            Assembly = assembly;
            Id = id;
            Name = name;
            IconResource = iconResource;
        }

        public static AgenticPlugIn? Get(Assembly assembly, Guid id, string name)
        {
            try
            {
                foreach (object attrib in assembly.GetCustomAttributes(false))
                {
                    Type? type = attrib?.GetType();
                    if (type is null) continue;

                    if (!string.Equals(type.Name, "PlugInDescriptionAttribute", StringComparison.OrdinalIgnoreCase)) continue;

                    // [assembly: PlugInDescription(DescriptionType.Icon, "Rhino.AI.Panel_light.ico")]

                    // DescriptionType -> 8
                    PropertyInfo? descInfo = type.GetProperty("DescriptionType");
                    int enumIntValue = (int)descInfo?.GetValue(attrib);
                    if (enumIntValue != 8) continue;

                    // Value -> is the resource
                    PropertyInfo? valueInfo = type.GetProperty("Value");
                    if (valueInfo?.GetValue(attrib) is not string resource) continue;

                    return new AgenticPlugIn(assembly, id, name, resource);
                }
            }
            catch { }

            return null;
        }
    }

}
