#if R9

using System.Reflection;
using System.Runtime.ExceptionServices;

using Rhino.AI.Tools;
using Rhino.PlugIns;
using Rhino.Runtime.Code.Languages;

namespace Rhino.AI;

// Registrar lives in RhinoCodePlatform.Rhino3D, which ships with the RhinoCode plug-in
// rather than with us, so it is reached reflectively once that plug-in is loaded.
// Registrar itself no-ops when the requested languages are already up, so callers are
// free to call this before every script run.
internal static class ScriptingEnvironment
{
    private static Guid RhinoCodePluginId { get; } = new("c9cba87a-23ce-4f15-a918-97645c05cde7");

    private static MethodInfo? Starter { get; set; }

    private static dynamic? CachedHost { get; set; }

    public static dynamic? Host => CachedHost ??= ResolveHost();

    public static IToolResult EnsurePythonRuntimeIsAvailable() => StartScriptingLanguages(LanguageSpec.Python3);

    internal static IToolResult EnsureCSharpRuntimeIsAvailable() => StartScriptingLanguages(LanguageSpec.CSharp);

    private static bool StartedPython { get; set; } = false;
    private static bool StartedCsharp { get; set; } = false;

    private static IToolResult StartScriptingLanguages(LanguageSpec spec)
    {
        if (!PlugIn.LoadPlugIn(RhinoCodePluginId, true, true))
            return Failure(ToolError.Unsupported, "The RhinoCode plug-in would not load, so no scripting language is available.");

        if (spec == LanguageSpec.Python3)
        {
            if (StartedPython) return Success();
            StartedPython = true;
            // TODO : Make this a debug line
            // RhinoApp.WriteLine("Loading Python 3 for Script Server");
        }

        if (spec == LanguageSpec.CSharp)
        {
            if (StartedCsharp) return Success();
            StartedCsharp = true;
            // TODO : Make this a debug line
            // RhinoApp.WriteLine("Loading C# for Script Server");
        }

        try
        {
            MethodInfo? starter = Starter ??= ResolveStarter();
            starter?.Invoke(null, [spec, true]);
            return Success();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            return Failure(ex);
        }
    }

    // Host is only needed to hand back to the project APIs, so we never name its type.
    private static dynamic? ResolveHost()
    {
        try
        {
            Type host = typeof(LanguageSpec).Assembly.GetType("Rhino.Runtime.Code.Platform.Host", throwOnError: true)!;

            return Activator.CreateInstance(host, "Rhino3D", RhinoApp.Version);
        }
        catch { }
        return null;
    }

    private static MethodInfo? ResolveStarter()
    {
        try
        {
            if (!PlugIn.LoadPlugIn(RhinoCodePluginId))
                return null;

            Type registrar = Type.GetType("RhinoCodePlatform.Rhino3D.Registrar, RhinoCodePlatform.Rhino3D", throwOnError: false)!;

            return registrar.GetMethod(
                       name: "StartScriptingLanguages",
                       bindingAttr: BindingFlags.Public | BindingFlags.Static,
                       binder: null,
                       types: [typeof(LanguageSpec), typeof(bool)],
                       modifiers: null);
        }
        catch { }
        return null;
    }
}

#endif
