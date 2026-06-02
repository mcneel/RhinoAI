using Rhino.PlugIns;

namespace RhMcp;

public class RhMcpPlugin : PlugIn
{
    private System.Threading.Timer? _poll;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        // Case 1: doc already available when plugin loads (e.g. scripted load)
        var doc = RhinoDoc.ActiveDoc;
        if (doc != null)
        {
            StartServer(doc);
            return base.OnLoad(ref errorMessage);
        }

        // Case 2: subscribe to all document-creation events
        RhinoApp.Initialized += OnAppInitialized;
        RhinoDoc.BeginOpenDocument += OnOpenDoc;
        RhinoDoc.NewDocument += OnNewDoc;

        // Case 3: poll fallback every 2 s in case events fire before subscription
        _poll = new System.Threading.Timer(_ =>
        {
            var d = RhinoDoc.ActiveDoc;
            if (d == null) return;
            _poll?.Dispose(); _poll = null;
            RhinoApp.Initialized -= OnAppInitialized;
            RhinoDoc.BeginOpenDocument -= OnOpenDoc;
            RhinoDoc.NewDocument -= OnNewDoc;
            StartServer(d);
        }, null, 2000, 2000);

        return base.OnLoad(ref errorMessage);
    }

    private void OnAppInitialized(object? sender, EventArgs e)
    {
        _poll?.Dispose(); _poll = null;
        RhinoApp.Initialized -= OnAppInitialized;
        RhinoDoc.BeginOpenDocument -= OnOpenDoc;
        RhinoDoc.NewDocument -= OnNewDoc;
        StartServer(RhinoDoc.ActiveDoc);
    }

    private void OnOpenDoc(object? sender, DocumentOpenEventArgs e)
    {
        _poll?.Dispose(); _poll = null;
        RhinoApp.Initialized -= OnAppInitialized;
        RhinoDoc.BeginOpenDocument -= OnOpenDoc;
        RhinoDoc.NewDocument -= OnNewDoc;
        StartServer(e.Document);
    }

    private void OnNewDoc(object? sender, DocumentEventArgs e)
    {
        _poll?.Dispose(); _poll = null;
        RhinoApp.Initialized -= OnAppInitialized;
        RhinoDoc.BeginOpenDocument -= OnOpenDoc;
        RhinoDoc.NewDocument -= OnNewDoc;
        StartServer(e.Document);
    }

    private static void StartServer(RhinoDoc? doc)
    {
        if (doc == null) return;

        string? portStr = Environment.GetEnvironmentVariable(MCPSpawnCommand.PortEnvVar);
        if (!string.IsNullOrEmpty(portStr)) return;

        try
        {
            int port = RhinoMcpHost.GetNextPort();
            if (RhinoMcpHost.StartOrRestart(doc, port, true))
            {
                RhinoApp.WriteLine("The Rhino MCP Platform is ready.");
                return;
            }
        }
        catch (Exception)
        {
            // Startup failure is non-fatal; user can invoke MCPStartCommand manually.
        }

        RhinoApp.WriteLine("The Rhino MCP Server failed to start");
    }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;
}
