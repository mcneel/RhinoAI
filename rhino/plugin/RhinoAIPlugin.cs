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

    // private CommandInterceptorHost? CommandInterceptors { get; set; }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        if (RouterStaging.EnsureStaged().StagingError is string stagingError)
        {
            HostUtils.LogDebugEvent($"RhinoAI: could not stage the MCP router ({stagingError}).\n");
        }
        if (!RhinoApp.IsRunningHeadless)
            Panels.RegisterPanel(this, typeof(UI.AIPanel), Localization.LocalizeString("AI", 1), LoadPanelIcon(), PanelType.PerDoc);

        AIInstances.Agents.LoadAgentsFromJson();

        // CommandInterceptors = new CommandInterceptorHost();
        RhinoAIHost.RegisterDocumentWatcher();

        // Headless hosts (Linux RhinoCore, Rhino.Inside, Compute) have no one to type MCPStart,
        // and they created their document before third-party plug-ins loaded, so the NewDocument
        // hook above never fires for it. Start on the existing document when the router's
        // autostart variable is set; its value is the port.
        if (RhinoApp.IsRunningHeadless
            && int.TryParse(Environment.GetEnvironmentVariable(MCPSpawnCommand.PortEnvVar), out int port)
            && RhinoDoc.ActiveDoc is RhinoDoc activeDoc)
        {
            // Several RhinoCore processes on one machine (Rhino.Compute children) share the
            // variable, so only the first can have the configured port; the rest fall back to
            // the next free one and announce it like any other listener.
            if (!RhinoAIHost.StartOrRestart(activeDoc, port, quiet: true))
            {
                if (RhinoAIHost.TryGetNextPort(out int fallback) && RhinoAIHost.StartOrRestart(activeDoc, fallback, quiet: true))
                    HostUtils.LogDebugEvent($"RhinoAI: port {port} was taken; headless MCP listener started on {fallback}.\n");
                else
                    HostUtils.LogDebugEvent($"RhinoAI: headless MCP listener failed to start on port {port}.\n");
            }
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
        // CommandInterceptors?.Dispose();
        AgentHost.Shutdown();
        ScriptProjects.ScriptProjectStartup.DeleteRegistryCache();
    }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

}
