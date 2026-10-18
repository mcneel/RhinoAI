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

    private const string RHINO_PATH = @"Software\McNeel\Rhinoceros";

    /// <summary>
    /// Prevents the AI Plugin being registered ANYWHERE.
    /// </summary>
    public static void DeleteRegistryCache()
    {
        if (!Runtime.HostUtils.RunningOnWindows) return;
        if (!TryReadProjectId(out Guid id)) return;

        using Microsoft.Win32.RegistryKey? rhino = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RHINO_PATH, writable: true);
        if (rhino is null) return;

        string key = id.ToString();
        foreach (string version in rhino.GetSubKeyNames())
        {
            // TODO : Ignore pre-9.0

            using Microsoft.Win32.RegistryKey? plugIns = rhino.OpenSubKey(version + @"\Plug-Ins", writable: true);
            plugIns?.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
        }
    }

    private static bool TryReadProjectId(out Guid id)
    {
        id = Guid.Empty;
        string projectFile = ScriptProjectPaths.For(null).ProjectFile;
        if (!File.Exists(projectFile)) return false;

        try
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(projectFile));
            if (!json.RootElement.TryGetProperty("id", out JsonElement element)) return false;
            element.TryGetGuid(out id);
            return id != Guid.Empty;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }

}
