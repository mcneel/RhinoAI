using System.Net;

using Eto.Forms;

namespace Rhino.AI.UI;

internal partial class AIPanelViewModel
{

    private HttpListener Listener { get; }

    public PanelBridge Bridge { get; }
    private WebView View { get; }
    private uint DocumentSerialNumber { get; }
    private RhinoDoc? Document => RhinoDoc.FromRuntimeSerialNumber(DocumentSerialNumber);

    public AIPanelViewModel(WebView view, uint documentSerialNumber)
    {
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
