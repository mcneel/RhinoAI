using System.IO;

using Rhino.AI.Tools;

namespace Rhino.AI.ScriptProjects;

// Preview-loaded commands last only for the session, so the previous build is re-previewed once per run.
internal static class ScriptProjectStartup
{
    private static bool Scheduled { get; set; }

    public static void ReloadWhenIdle()
    {
        if (!AISettings.AutoLoadScriptPlugIn) return;
        if (Scheduled) return;
        Scheduled = true;
        
        if (!ScriptProjectRunner.IsSupportedRhino) return;
        
        RhinoApp.Initialized += Initialized;
    }

    private static void Initialized(object? _, EventArgs __)
    {
        RhinoApp.Initialized -= Initialized;
        RhinoApp.Idle += Idle;
    }

    private static void Idle(object? _, EventArgs __)
    {
        if (RhinoDoc.ActiveDoc is null) return;
        RhinoApp.Idle -= Idle;

        // Ignore if no Project exists
        if (!File.Exists(ScriptProjectPaths.For(null).ProjectFile)) return;

        try
        {
            IToolResult result = ScriptProjectRunner.Reload();
            if (result.Error is not null)
            {
                RhinoApp.WriteLine($"Rhino AI could not load your custom commands from the last session");
            }
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Rhino AI could not reload your script commands: {ex.Message}");
        }
    }
}
