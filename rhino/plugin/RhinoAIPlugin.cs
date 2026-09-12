using System.IO;
using System.Reflection;

using Rhino.PlugIns;
using Rhino.Runtime;

namespace Rhino.AI;

public class RhinoAIPlugin : PlugIn
{
    private const string IconResourceName = "Rhino.AI.Panel_dark.ico";
    private const string DarkIconResourceName = "Rhino.AI.Panel_dark.ico";

    private CommandInterceptorHost? CommandInterceptors { get; set; }

    public bool WasStartedViaAgent { get; private set; }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        Rhino.UI.Panels.RegisterPanel(this, typeof(AIPanel), Rhino.UI.LOC.STR("AI"), LoadPanelIcon(), Rhino.UI.PanelType.PerDoc);

        WasStartedViaAgent = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(MCPSpawnCommand.PortEnvVar));

        if (WasStartedViaAgent || AIAutoLoad.ShouldAutoLoad())
        {
            CommandInterceptors = new CommandInterceptorHost();

            RhinoDoc.NewDocument += RegisterNew;
            RhinoDoc.EndOpenDocument += RegisterOpen;
        }

        return base.OnLoad(ref errorMessage);
    }

    // Adds the "AI" settings page to the Rhino Options dialog. Called each time Options is opened, so a
    // fresh page (and panel) is built per open and its state reflects the current settings.
    protected override void OptionsDialogPages(List<Rhino.UI.OptionsDialogPage> pages)
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

    private void RegisterNew(object? sender, DocumentEventArgs e) => Register(e.Document);

    private void RegisterOpen(object? sender, DocumentOpenEventArgs e)
    {
        if (e.Merge) return;
        if (e.Reference) return;
        Register(e.Document);
    }

    private void Register(RhinoDoc? doc)
    {
        if (doc is null) return;

        RhinoDoc.NewDocument -= RegisterNew;
        RhinoDoc.EndOpenDocument -= RegisterOpen;

        if (!WasStartedViaAgent)
        {
            if (!RhinoAIHost.TryGetNextPort(out int port))
            {
                RhinoApp.WriteLine("RhinoAI's MCP server failed to start: no free port available.");
            }
            else if (!RhinoAIHost.StartOrRestart(doc, port, true))
            {
                RhinoApp.WriteLine("RhinoAI's MCP Server failed to start");
            }
        }

        RhinoAIHost.RegisterDocumentWatcher();

        ScriptProjects.ScriptProjectStartup.ReloadWhenIdle();
    }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

}
