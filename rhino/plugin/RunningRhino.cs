using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RhinoAI;

// Gated on the host RhinoCommon being a Debug build (JIT optimizer disabled), not on install location or Debugger.IsAttached: a release Rhino runs fine from anywhere (Downloads, a mounted volume) and RhinoLocator finds it, whereas only a developer's debug Rhino needs the router pointed back at it instead of the installed release.
internal static class RunningRhino
{
    public static (string EnvVar, string Path)? SourceBuildOverride { get; } = Detect();

    private static (string, string)? Detect()
    {
        try
        {
            if (!RhinoIsDebugBuild)
                return null;

            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return null;

            string? target = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? EnclosingAppBundle(exe) : exe;
            return target is null ? null : (EnvVar, target);
        }
        catch
        {
            return null;
        }
    }

    private static bool RhinoIsDebugBuild =>
        typeof(Rhino.RhinoApp).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled ?? false;

    private static string EnvVar => $"RHINO_MCP_RHINO_EXE_{RhinoVersion.Token}";

    private static string? EnclosingAppBundle(string exePath)
    {
        for (DirectoryInfo? dir = new FileInfo(exePath).Directory; dir is not null; dir = dir.Parent)
        {
            if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
        }
        return null;
    }
}
