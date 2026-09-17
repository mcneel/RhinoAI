using System.Net;

using Eto.Forms;

namespace Rhino.AI.UI;

internal partial class AIPanelViewModel
{

    private HttpListener Listener { get; }

    public PanelBridge Bridge { get; }
    private WebView View { get; }
    private uint DocumentSerialNumber { get; }

    public AIProfile Profile { get; }

    // The docked panel is one per document and keeps to its own. The assistants live in a window
    // that is one for the whole application — the Script Editor's, Grasshopper's — so they follow
    // whatever document is in front, as those windows do, and fall back to the one they were made
    // with when there is no active document at all.
    private RhinoDoc? Document =>
        Profile == AIProfile.Rhino
            ? RhinoDoc.FromRuntimeSerialNumber(DocumentSerialNumber)
            : RhinoDoc.ActiveDoc ?? RhinoDoc.FromRuntimeSerialNumber(DocumentSerialNumber);

    public AIPanelViewModel(WebView view, uint documentSerialNumber, AIProfile profile)
    {
        Profile = profile;
        System.Net.Sockets.TcpListener tcp = new(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();

        Listener = new();
        // localhost, not 127.0.0.1: Windows HTTP.SYS grants that host to unelevated processes without a URL reservation.
        Listener.Prefixes.Add($"http://localhost:{port}/");

        View = view;
        Bridge = new PanelBridge(view, IncomingCommand);
        DocumentSerialNumber = documentSerialNumber;

        StartPageServer();
    }

}
