using System;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Rhino.AI.PlugIns;

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
