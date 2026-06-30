using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using Rhino.PlugIns;

namespace RhMcp;

public class RhMcpPlugin : PlugIn
{
    private const string IconResourceName = "RhMcp.logo.svg";

    private CommandInterceptorHost? CommandInterceptors { get; set; }

    // Cancelled on shutdown so any startup background work stops cleanly with Rhino.
    private CancellationTokenSource Shutdown { get; } = new();

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        RhinoDoc.BeginOpenDocument += Register;
        RhinoDoc.CloseDocument += DeRegister;
        CommandInterceptors = new CommandInterceptorHost();

        // Probe agent install paths once on load so the active agent resolves before the first
        // prompt; Part 1's settings dialog re-runs this when the agent config changes.
        AgentRegistry.Refresh();

        // Wire the bundled rhino MCP server into any MCP-aware tools the user already has, so
        // external agents work out of the box. Background: never block or fail OnLoad.
        McpClientConfigInstaller.InstallInBackground(Shutdown.Token);

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
            Assembly assembly = typeof(RhMcpPlugin).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(IconResourceName);
            if (stream is null)
                return null;

            using StreamReader reader = new(stream);
            string svg = reader.ReadToEnd();

            Size size = Rhino.UI.Panels.IconSizeInPixels;
            int pixels = size.Width > 0 ? size.Width : 36;
            using Bitmap bitmap = Rhino.UI.DrawingUtilities.BitmapFromSvg(svg, pixels, pixels, adjustForDarkMode: true);
            return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
        }
        catch
        {
            return null;
        }
    }

    protected override void OnShutdown()
    {
        Shutdown.Cancel();
        Shutdown.Dispose();
        CommandInterceptors?.Dispose();
        AgentHost.Shutdown();
    }

    private void Register(object? sender, DocumentOpenEventArgs e)
    {
        RhinoDoc.BeginOpenDocument -= Register;

        string? portStr = Environment.GetEnvironmentVariable(MCPSpawnCommand.PortEnvVar);
        if (!string.IsNullOrEmpty(portStr)) return;

        if (!RhinoMcpHost.TryGetNextPort(out int port))
        {
            RhinoApp.WriteLine("The Rhino MCP Server failed to start: no free port available.");
            return;
        }

        try
        {
            if (RhinoMcpHost.StartOrRestart(e.Document, port, true))
            {
                RhinoApp.WriteLine("The Rhino MCP connection is ready.");
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
            RhinoMcpHost.Stop(e.Document);
        }
        catch
        {
        }
    }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

}
