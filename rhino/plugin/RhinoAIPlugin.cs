using System.IO;
using System.Reflection;

using Rhino.PlugIns;
using Rhino.Runtime;
using Rhino.UI;

namespace Rhino.AI;

public class RhinoAIPlugin : PlugIn
{
    private const string IconResourceName = "Rhino.AI.Panel_dark.ico";
    private const string DarkIconResourceName = "Rhino.AI.Panel_dark.ico";

    private CommandInterceptorHost? CommandInterceptors { get; set; }

    public bool WasStartedViaAgent { get; private set; }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        if (RouterStaging.EnsureStaged().StagingError is string stagingError)
            RhinoApp.WriteLine($"RhinoAI: could not stage the MCP router ({stagingError}).");

        Panels.RegisterPanel(this, typeof(UI.AIPanel), LOC.STR("AI"), LoadPanelIcon(), PanelType.PerDoc);

        WasStartedViaAgent = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(MCPSpawnCommand.PortEnvVar));

        if (WasStartedViaAgent || AIAutoLoad.ShouldAutoLoad())
        {
            CommandInterceptors = new CommandInterceptorHost();
            RhinoAIHost.RegisterDocumentWatcher();
        }
        
        ScriptProjects.ScriptProjectStartup.ReloadWhenIdle();

        return base.OnLoad(ref errorMessage);
    }

    // Adds the "AI" settings page to the Rhino Options dialog. Called each time Options is opened, so a
    // fresh page (and panel) is built per open and its state reflects the current settings.
    protected override void OptionsDialogPages(List<OptionsDialogPage> pages)
    {
        pages.Add(new AIOptionsPage());
    }

    // GetHicon isn't guaranteed on every platform, so fall back to no icon rather than fail OnLoad.
    private static System.Drawing.Icon? LoadPanelIcon()
    {
        try
        {
            Assembly assembly = typeof(RhinoAIPlugin).Assembly;

            string resourceName = HostUtils.RunningInDarkMode ? DarkIconResourceName : IconResourceName;
            using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream is null)
                return null;

            return new System.Drawing.Icon(resourceStream);
        }
        catch
        {
            return null;
        }
    }

    protected override void OnShutdown()
    {
        CommandInterceptors?.Dispose();
        AgentHost.Shutdown();
    }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

}
