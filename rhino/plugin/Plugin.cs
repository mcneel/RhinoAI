using System.Drawing;
using System.IO;
using System.Reflection;
using Rhino.PlugIns;

namespace RhMcp;

public class RhMcpPlugin : PlugIn
{
    private const string IconResourceName = "RhMcp.logo.svg";

    // One instance for the process, because the registry behind it is process-global.
    private readonly Server.Extensibility.McpExtensionHost _extensionHost = new();

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
            RhinoMcpHost.Stop(e.Document);
        }
        catch
        {
        }
    }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    /// <summary>
    /// The extension point other Rhino plug-ins use to contribute MCP tools to this
    /// server at run time.
    /// </summary>
    /// <returns>
    /// The <see cref="Server.Extensibility.McpExtensionHost"/> singleton. Callers reach it
    /// with <c>RhinoApp.GetPlugInObject(2668d7ed-f507-4a68-8295-8172147a0e39)</c> and use
    /// it by reflection, since there is no assembly for them to reference.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Available as soon as this plug-in is loaded, which is earlier than the MCP server
    /// itself starts -- that happens on the first document open. Registering before then is
    /// fine: the dispatcher reads the registry live on each request rather than caching a
    /// snapshot at start-up.
    /// </para>
    /// <para>
    /// Because <c>RhinoApp.GetPlugInObject</c> loads its target, a caller reaching for this
    /// is what loads this plug-in, so neither side needs the other to have loaded first.
    /// Callers should still do it from idle rather than their own <c>OnLoad</c>, to avoid a
    /// reentrant plug-in load inside Rhino's plug-in manager.
    /// </para>
    /// </remarks>
    public override object GetPlugInObject() => _extensionHost;

}
