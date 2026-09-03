using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

using Eto.Forms;

using RhinoAI.WebPanel;

namespace RhinoAI;

// The AI panel rendered in a WebView instead of Eto controls. One instance per document, mirroring
// AIPAnel, so both can be registered side by side while this is evaluated.
//
// The panel owns three things and nothing else: the WebView, a PanelBridge that carries the
// protocol over it, and a ConversationFeed that turns Conversation.Changed into incremental events.
// Every action the user takes arrives as a PanelCommand and is routed to the same AgentHost /
// AgentDispatch entry points the Eto panel uses, so there is no second code path for behaviour.
[Guid("b7d61e2f-4c1a-4a5e-9f4b-2c9a1d0e6f31")]
public class WebAIPanel : Panel
{
    public static Guid PanelId => typeof(WebAIPanel).GUID;

    private const string PageResource = "RhinoAI.panel.html";

    private uint DocSerial { get; }
    private WebView View { get; } = new();
    private PanelBridge Bridge { get; }

    private ConversationFeed? Feed { get; set; }
    private Conversation? Subscribed { get; set; }
    private Action? SubscribedHandler { get; set; }

    private string LastAgentKey { get; set; } = string.Empty;

    internal Action<Conversation> PersistConversationHook { get; set; } = ConversationStore.Save;

    public WebAIPanel()
        : this(RhinoDoc.ActiveDoc is { } doc ? doc.RuntimeSerialNumber : 0u)
    {
    }

    public WebAIPanel(uint documentSerialNumber)
    {
        DocSerial = documentSerialNumber;
        Bridge = new PanelBridge(View, Handle);
        Content = View;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        Assembly assembly = typeof(WebAIPanel).Assembly;
        using Stream? page = assembly.GetManifestResourceStream(PageResource);
        if (page is null)
        {
            RhinoApp.WriteLine($"[rhino-ai] panel resource '{PageResource}' is missing from the plug-in.");
            return;
        }
        View.LoadHtml(page);
    }

    // The Conversation is owned by the pooled agent and outlives this panel, so its off-thread
    // Changed event would keep firing into a disposed WebView. Detach on unload.
    protected override void OnUnLoad(EventArgs e)
    {
        Unsubscribe();
        base.OnUnLoad(e);
    }

    // ------------------------------------------------------------------ commands

    private void Handle(PanelCommand command)
    {
        switch (command)
        {
            case ReadyCommand:
                SendEnvironment();
                Resubscribe();
                Feed?.Replay();
                break;

            case PromptCommand prompt:
                Send(prompt.Request.Text);
                break;

            case CancelCommand:
                if (TryDoc(out RhinoDoc cancelDoc) && AgentHost.TryFor(cancelDoc, out IAgentRunner running))
                    running.Cancel();
                break;

            case NewConversationCommand:
                NewConversation();
                break;

            case SelectAgentCommand select:
                SelectAgent(select.Name);
                break;

            case AnswerQuestionCommand answer:
                Answer(answer.Id, answer.Answers);
                break;

            case DismissQuestionCommand dismiss:
                Dismiss(dismiss.Id);
                break;

            case OpenSettingsCommand:
                OpenSettings();
                break;

            case OpenUrlCommand open:
                Application.Instance.Open(open.Url);
                break;

            case ClipboardCommand copy:
                Clipboard.Instance.Text = copy.Text;
                break;
        }
    }

    private void Send(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !TryDoc(out RhinoDoc doc))
            return;

        // Resolve before dispatch so the Changed hook is attached before the reader loop starts
        // writing, and so a prompt is never silently dropped when no agent is available.
        if (!AgentHost.TryFor(doc, out IAgentRunner _))
        {
            Bridge.Post(new NoticeEvent("error", "No AI agent available. Open AI settings to configure one."));
            return;
        }

        Resubscribe();
        AgentDispatch.PromptActive(doc, UserMessage.FromText(text.Trim()));
    }

    private void NewConversation()
    {
        if (TryConversation(out Conversation current))
            PersistConversationHook(current);

        // Dropping the pooled agent is what starts a new session; Stop also forgets the pinned
        // agent, so re-pin the user's choice or New silently reverts to the configured default.
        if (TryDoc(out RhinoDoc doc))
        {
            AgentHost.Stop(doc);
            if (LastAgentKey.Length > 0)
                AgentHost.SetActive(doc, LastAgentKey);
        }

        Resubscribe();
        Feed?.Replay();
    }

    private void SelectAgent(string name)
    {
        if (!TryDoc(out RhinoDoc doc))
            return;
        if (AgentRegistry.Chain.FirstOrDefault(r => r.Definition.Name == name) is not { Definition.Enabled: true, Available: true })
            return;

        LastAgentKey = name;
        AgentHost.SetActive(doc, name);
        Resubscribe();
        SendAgents();
        Feed?.Replay();
    }

    private void Answer(string id, IReadOnlyList<string> answers)
    {
        if (Feed is null || !Feed.TryResolveQuestion(id, out PendingQuestion question) || !TryDoc(out RhinoDoc doc))
            return;

        if (answers.Count == 0)
        {
            Dismiss(id);
            return;
        }

        // First-wins through the same claim the command-line picker uses: losing it means the picker
        // already dispatched this question, so there is nothing left to do but re-render.
        if (AskUserPicker.TryClaim(doc.RuntimeSerialNumber, question))
            AgentDispatch.AnswerActive(doc, UserMessage.FromText(string.Join(", ", answers)));

        if (TryConversation(out Conversation convo))
            convo.ClearPendingQuestion(question);
    }

    private void Dismiss(string id)
    {
        if (Feed is null || !Feed.TryResolveQuestion(id, out PendingQuestion question))
            return;

        if (TryDoc(out RhinoDoc doc))
            AskUserPicker.Cancel(doc.RuntimeSerialNumber, question);
        if (TryConversation(out Conversation convo))
            convo.ClearPendingQuestion(question);
    }

    private void OpenSettings()
    {
        AISettingsDialog dialog = new();
        dialog.ShowModal(this);
        AgentRegistry.Refresh();
        SendAgents();
    }

    // ------------------------------------------------------------------ outbound

    private void SendEnvironment()
    {
        Bridge.Post(new HelloEvent(new PanelHost(
            "Rhinoceros",
            RhinoApp.Version.ToString(),
            Environment.OSVersion.Platform == PlatformID.Unix ? "macos" : "windows",
            TryDoc(out RhinoDoc doc) ? DocTitle(doc) : "Untitled",
            new PanelCapabilities(Attachments: false, ViewportCapture: true, UndoTurn: false, Grasshopper: true))));

        Bridge.Post(new ThemeEvent(IsDarkTheme() ? "dark" : "light"));
        SendAgents();
    }

    private void SendAgents()
    {
        List<PanelAgent> agents = new();
        foreach (ResolvedAgent resolved in AgentRegistry.Chain)
        {
            bool enabled = resolved.Definition.Enabled;
            string availability = !enabled ? "disabled" : !resolved.Available ? "missing" : "ready";
            string model = resolved.Definition.Model.Length > 0 ? resolved.Definition.Model : "default";
            agents.Add(new PanelAgent(
                resolved.Definition.Name,
                PrettyName.Of(resolved.Definition.Name),
                model,
                PrettyName.Of(model),
                availability,
                availability switch
                {
                    "disabled" => "turned off in AI settings",
                    "missing" => $"'{resolved.Definition.Command}' was not found",
                    _ => null,
                },
                resolved.Definition.IsBuiltin));
        }

        string? active = null;
        if (TryDoc(out RhinoDoc doc) && AgentHost.TryFor(doc, out IAgentRunner agent))
            active = agent.Name;
        else
            active = AgentRegistry.Chain.FirstOrDefault(static r => r.Definition.Enabled && r.Available)?.Definition.Name;

        if (active is { Length: > 0 })
            LastAgentKey = active;

        Bridge.Post(new AgentsEvent(agents, active));
    }

    // ------------------------------------------------------------------ plumbing

    private void Resubscribe()
    {
        Unsubscribe();

        if (!TryConversation(out Conversation convo))
            return;

        Feed = new ConversationFeed(convo, Bridge.Post);

        // Off-thread (the agent's reader loop). Eto and the WebView are UI-thread only.
        Action handler = () => RhinoApp.InvokeOnUiThread(new Action(PumpIfLoaded));
        convo.Changed += handler;
        Subscribed = convo;
        SubscribedHandler = handler;
    }

    private void Unsubscribe()
    {
        if (Subscribed is not null && SubscribedHandler is not null)
            Subscribed.Changed -= SubscribedHandler;
        Subscribed = null;
        SubscribedHandler = null;
        Feed = null;
    }

    // A queued Pump can land after the panel is unloaded; touching a detached WebView risks an
    // AppKit abort, so Loaded gates it the same way the Eto panel gates its Rerender.
    private void PumpIfLoaded()
    {
        if (Loaded)
            Feed?.Pump();
    }

    // Resolve only this panel's own document. No ActiveDoc fallback: a PerDoc panel whose document
    // has vanished must no-op, never act on whatever document happens to be active now.
    private bool TryDoc(out RhinoDoc doc)
    {
        if (RhinoDoc.FromRuntimeSerialNumber(DocSerial) is { } own)
        {
            doc = own;
            return true;
        }
        doc = default!;
        return false;
    }

    private bool TryConversation(out Conversation convo)
    {
        if (TryDoc(out RhinoDoc doc) && AgentHost.TryFor(doc, out IAgentRunner agent))
        {
            convo = agent.Conversation;
            return true;
        }
        convo = default!;
        return false;
    }

    private static string DocTitle(RhinoDoc doc) =>
        string.IsNullOrEmpty(doc.Path) ? "Untitled" : Path.GetFileName(doc.Path);

    private static bool IsDarkTheme()
    {
        Eto.Drawing.Color background = Eto.Drawing.SystemColors.ControlBackground;
        return background.R + background.G + background.B < 1.5f;
    }
}
