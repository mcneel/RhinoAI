using System.Threading.Tasks;

using Eto.Forms;

using Rhino.Runtime;

namespace Rhino.AI.UI;

internal class PanelBridge
{

    public string Id { get; } = Guid.NewGuid().ToString();

    private WebView View { get; }

    private Action<PanelCommand?> CommandReceiver { get; }

    public PanelBridge(WebView view, Action<PanelCommand?> commandReceiver)
    {
        View = view;
        CommandReceiver = commandReceiver;
        View.MessageReceived += HandleReceived;
        View.DocumentLoading += HandleLoading;
        View.DocumentLoaded += HandleBackLog;
    }

    private void HandleReceived(object? _, WebViewMessageEventArgs e)
    {
        try
        {
            PanelCommand? command = PanelJson.Deserialize(e.Message);
            CommandReceiver?.Invoke(command);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            HostUtils.LogDebugEvent($"[rhino-ai] panel sent a command this build does not handle: {ex.Message}.\n");
            return;
        }
    }

    // Control.Loaded means the control is attached, not that a page exists, so the document is tracked separately.
    private bool IsPageLoaded { get; set; }

    private void HandleLoading(object? _, WebViewLoadingEventArgs e) => IsPageLoaded = false;

    private void HandleBackLog(object? _, WebViewLoadedEventArgs e)
    {
        IsPageLoaded = true;
        while (Backlog.TryDequeue(out PanelEvent? @event))
        {
            if (@event is null) continue;
            Post(@event);
        }
    }

    private Queue<PanelEvent> Backlog { get; } = new();
    public void Post(PanelEvent value)
    {
        if (!IsPageLoaded)
        {
            Backlog.Enqueue(value);
            return;
        }

        string script = $"window.rhinoAI && window.rhinoAI.receive({PanelJson.Serialize(value)});";
        Task task = View.ExecuteScriptAsync(script);
        task.ContinueWith(
            static t => HostUtils.LogDebugEvent($"[rhino-ai] panel script failed: {t.Exception?.GetBaseException().Message}.\n"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

}
