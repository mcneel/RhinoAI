using System.IO;
using System.Reflection;

using Rhino.PlugIns;

namespace RhinoAI;

public class RhinoAIPlugin : PlugIn
{
    private const string IconResourceName = "RhinoAI.logo.svg";

    private CommandInterceptorHost? CommandInterceptors { get; set; }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        RhinoDoc.BeginOpenDocument += Register;
        RhinoDoc.CloseDocument += DeRegister;
        CommandInterceptors = new CommandInterceptorHost();

        // Probe agent install paths once on load so the active agent resolves before the first
        // prompt; Part 1's settings dialog re-runs this when the agent config changes.
        AgentRegistry.Refresh();

        Rhino.UI.Panels.RegisterPanel(this, typeof(AIPAnel), "AI", LoadPanelIcon(), Rhino.UI.PanelType.PerDoc);
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
            System.Drawing.Icon icon = Rhino.UI.DrawingUtilities.IconFromResource(IconResourceName, assembly);
            return icon;
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

    private void Register(object? sender, DocumentOpenEventArgs e)
    {
        RhinoDoc.BeginOpenDocument -= Register;

        string? portStr = Environment.GetEnvironmentVariable(MCPSpawnCommand.PortEnvVar);
        if (!string.IsNullOrEmpty(portStr)) return;

        if (!RhinoAIHost.TryGetNextPort(out int port))
        {
            RhinoApp.WriteLine("The Rhino MCP Server failed to start: no free port available.");
            return;
        }

        try
        {
            if (RhinoAIHost.StartOrRestart(e.Document, port, true))
            {
                RhinoApp.WriteLine("The Rhino MCP Platform is ready.");
                return;
            }
        }
        catch
        {
        }

        RhinoApp.WriteLine("The Rhino MCP Server failed to start");
    }

    private void DeRegister(object? sender, DocumentEventArgs e)
    {
        RhinoDoc.BeginOpenDocument -= Register;

        try
        {
            RhinoAIHost.Stop(e.Document);
        }
        catch
        {
        }
    }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

}
