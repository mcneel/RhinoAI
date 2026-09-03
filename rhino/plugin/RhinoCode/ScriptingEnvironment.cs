#if RHINOCODE

using System.Reflection;
using System.Runtime.ExceptionServices;

using Rhino.PlugIns;

namespace RhinoAI;

// Registrar lives in RhinoCodePlatform.Rhino3D, which ships with the RhinoCode plug-in
// rather than with us, so it is reached reflectively once that plug-in is loaded.
// Registrar itself no-ops when the requested languages are already up, so callers are
// free to call this before every script run.
internal static class ScriptingEnvironment
{
    private static readonly Guid RhinoCodePluginId = new Guid("c9cba87a-23ce-4f15-a918-97645c05cde7");

    private static MethodInfo? Starter { get; set; }

    public static void EnsurePythonRuntimeIsAvailable() => StartScriptingLanguages(LanguageSpec.Python3);

    internal static void EnsureCSharpRuntimeIsAvailable() => StartScriptingLanguages(LanguageSpec.CSharp);

    private static bool StartedPython { get; set; } = false;
    private static bool StartedCsharp { get; set; } = false;

    private static void StartScriptingLanguages(ScriptProjects.Lang spec)
    {
        if (spec == ScriptProjects.Lang.Python3)
        {
            if (StartedPython) return;
            StartedPython = true;
            RhinoApp.WriteLine("Loading Python 3 for Script Server");
        }

        if (spec == ScriptProjects.Lang.CSharp)
        {
            if (StartedCsharp) return;
            StartedCsharp = true;
            RhinoApp.WriteLine("Loading C# for Script Server");
        }

        try
        {
            MethodInfo? starter = Starter ??= ResolveStarter();
            starter?.Invoke(null, [spec, true]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
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
