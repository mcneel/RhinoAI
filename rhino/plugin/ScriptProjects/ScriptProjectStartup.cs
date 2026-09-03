using System.IO;

namespace RhinoAI.ScriptProjects;

// Preview-loaded commands last only for the session, so the previous build is re-previewed once per run.
internal static class ScriptProjectStartup
{
    private static bool Scheduled { get; set; }

    public static void ReloadWhenIdle()
    {
        if (Scheduled || !ScriptProjectRunner.IsSupportedRhino)
            return;

        // Ignore if no Project exists
        if (!File.Exists(ScriptProjectPaths.For(null)?.ProjectFile)) return;

        Scheduled = true;
        RhinoApp.Idle += Reload;
    }

    private static void Reload(object? sender, EventArgs e)
    {
        RhinoApp.Idle -= Reload;

        if (RhinoDoc.ActiveDoc is not RhinoDoc doc)
            return;

        try
        {
            ReturnResult result = ScriptProjectRunner.Reload();
            if (!result)
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
