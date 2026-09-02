
namespace RhinoAI;

internal static class ScriptingEnvironment
{
    
    public static void EnsurePythonRuntimeIsAvailable()
    {
        RhinoCodePlatform.Rhino3D.Registrar.StartScriptingLanguages(Rhino.Runtime.Code.Languages.LanguageSpec.Python3);
    }

    internal static void EnsureCSharpRuntimeIsAvailable()
    {
        RhinoCodePlatform.Rhino3D.Registrar.StartScriptingLanguages(Rhino.Runtime.Code.Languages.LanguageSpec.CSharp);
    }

}
