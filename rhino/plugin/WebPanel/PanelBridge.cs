using System.Threading.Tasks;

using Eto.Forms;

namespace RhinoAI.WebPanel;

// Carries the protocol over Eto's WebView.
//
// Eto normalises both platforms for us: it injects window.eto.postMessage (wrapping
// webkit.messageHandlers on macOS and chrome.webview on Windows) and raises MessageReceived with the
// string, so the panel -> host direction is one code path. Host -> panel has to go through
// ExecuteScript, because Eto does not surface WebView2's PostWebMessageAsJson.
//
// Swapping this for a WebSocket on the Kestrel server the plugin already runs would remove the
// script-injection round trip and the UI-thread hop; nothing above this class would change.
internal sealed class PanelBridge
{
    private WebView View { get; }
    private Action<PanelCommand> Dispatch { get; }

    // Events raised before the document finishes loading would evaluate against a page with no
    // window.rhinoAI, so they wait here rather than being dropped.
    private Queue<string> Backlog { get; } = new();
    private bool Loaded { get; set; }

    public PanelBridge(WebView view, Action<PanelCommand> dispatch)
    {
        View = view;
        Dispatch = dispatch;

        View.DocumentLoaded += (_, _) =>
        {
            Loaded = true;
            while (Backlog.Count > 0)
                Run(Backlog.Dequeue());
        };

        View.MessageReceived += (_, e) => Receive(e.Message);
    }

    public void Post(PanelEvent value)
    {
        // The payload is embedded in JavaScript source, so it has to be valid JS as well as valid
        // JSON. System.Text.Json's default encoder escapes non-ASCII, which covers U+2028/U+2029:
        // legal inside a JSON string, but line terminators to a JS parser.
        string script = $"window.rhinoAI && window.rhinoAI.receive({PanelJson.Serialize(value)});";

        if (!Loaded)
        {
            Backlog.Enqueue(script);
            return;
        }
        Run(script);
    }

    private void Run(string script)
    {
        Task task = View.ExecuteScriptAsync(script);
        // Fire and forget, but never silently: an unobserved failure here means the panel has
        // quietly stopped updating, which is worse than a line in the command history.
        _ = task.ContinueWith(
            static t => RhinoApp.WriteLine($"[rhino-ai] panel script failed: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void Receive(string message)
    {
        PanelCommand? command;
        try
        {
            command = PanelJson.Deserialize(message);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // The panel sends commands this build has no case for yet. Ignoring them keeps a newer
            // panel usable against an older plugin instead of faulting the message pump.
            RhinoApp.WriteLine($"[rhino-ai] panel sent a command this build does not handle: {ex.Message}");
            return;
        }

        if (command is not null)
            Dispatch(command);
    }
}
