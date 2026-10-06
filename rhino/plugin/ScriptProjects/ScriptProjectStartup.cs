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
    private const string PROXY_ID = "f3e1f51e-f7f3-414a-99cd-5ebc88ec0ef5";

    /// <summary>
    /// Prevents the AI Plugin being registered ANYWHERE.
    /// </summary>
    public static void DeleteRegistryCache()
    {
        if (!Runtime.HostUtils.RunningOnWindows) return;
        if (ScriptProjectRunner.TryCreate(out IProjectRunner runner).IsFailure) return;
        if (!runner.TryGetProjectCommandNames(out List<string> commandNames)) return;
        
        string? id = runner.Id?.ToString();
        if (string.IsNullOrEmpty(id)) return;

        using Microsoft.Win32.RegistryKey? rhino = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RHINO_PATH, writable: true);
        if (rhino is null) return;

        HashSet<string> commands = new (commandNames);

        foreach (string version in rhino.GetSubKeyNames())
        {
            // TODO : Ignore pre-9.0

            using Microsoft.Win32.RegistryKey? plugIns = rhino.OpenSubKey(version + @"\Plug-Ins", writable: true);
            if (plugIns is null) continue;

            using Microsoft.Win32.RegistryKey? aiPlugIn = plugIns.OpenSubKey(id);
            if (aiPlugIn is not null)
            {
                plugIns.DeleteSubKeyTree(id, throwOnMissingSubKey: false);
            }

            // NOTE : Only add if necessary
            //using Microsoft.Win32.RegistryKey? proxyPlugIn = plugIns.OpenSubKey(PROXY_ID + @"\CommandList", writable: true);
            //if (proxyPlugIn is not null)
            //{
            //    foreach (string name in proxyPlugIn.GetValueNames())
            //    {
            //        if (!commands.Contains(name)) continue;
            //        proxyPlugIn.DeleteValue(name, throwOnMissingValue: false);
            //    }
            //}
        }
    }
    
}
