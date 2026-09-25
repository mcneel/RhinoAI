using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Rhino.AI.PlugIns;

public static class PlugInRegistry
{

    private record PlugInPermission(bool Allowed);

    private static Dictionary<PlugInToken, PlugInPermission> Permissions { get; } = [];

    /// <summary>
    /// 
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization | MethodImplOptions.Synchronized)]
    public static PlugInToken? Register()
    {
        try
        {
            Assembly assembly = Assembly.GetCallingAssembly();

            GuidAttribute guidAttribute = assembly.GetCustomAttribute<GuidAttribute>()
                ?? throw new InvalidOperationException($"{assembly.FullName} has no [assembly: Guid] attribute");

            Guid id = Guid.Parse(guidAttribute.Value);
            if (id == Guid.Empty) return null;

            string name = assembly.GetName()?.Name ?? assembly.FullName ?? id.ToString();

            PlugInToken permission = new(id, name);
            Permissions.Add(permission, new PlugInPermission(false));

            return permission;
        }
        catch { }

        return null;
    }

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
