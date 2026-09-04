using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

using Eto.Forms;

using RhinoAI.WebPanel;

namespace RhinoAI;

// The AI panel. One instance per document, rendered in a WebView: the panel itself lives in
// rhino/panel and is embedded as a single HTML resource.
//
// The GUID and type name are inherited from the Eto panel this replaced, so Rhino's saved panel
// layouts keep resolving and users' docked arrangements survive the swap.
//
// The panel owns three things and nothing else: the WebView, a PanelBridge that carries the
// protocol over it, and a ConversationFeed that turns Conversation.Changed into incremental events.
// Every action the user takes arrives as a PanelCommand and is routed to the same AgentHost /
// AgentDispatch entry points the Eto panel uses, so there is no second code path for behaviour.
[Guid("fb948c98-5987-45a3-8dcb-2814ed77ee3b")]
public class AIPAnel : Panel
{
    public static Guid PanelId => typeof(AIPAnel).GUID;

    private const string PageResource = "RhinoAI.panel.html";

    private uint DocSerial { get; }
    private WebView View { get; } = new();
    private PanelBridge Bridge { get; }

    private ConversationFeed? Feed { get; set; }

    // Non-null while a saved transcript is on screen. Live Changed events are ignored until Back,
    // so a running agent cannot redraw over what the user is reading.
    private ConversationFeed? Review { get; set; }
    private Conversation? Subscribed { get; set; }
    private Action? SubscribedHandler { get; set; }

    private string LastAgentKey { get; set; } = string.Empty;

    // Fingerprint of the last theme sent, so repeated settings changes are not re-broadcast.
    private string LastTheme { get; set; } = string.Empty;

    internal Action<Conversation> PersistConversationHook { get; set; } = ConversationStore.Save;

    public AIPAnel()
        : this(RhinoDoc.ActiveDoc is { } doc ? doc.RuntimeSerialNumber : 0u)
    {
    }

    public AIPAnel(uint documentSerialNumber)
    {
        DocSerial = documentSerialNumber;
        Bridge = new PanelBridge(View, Handle);
        Shown += (_, _) =>
        {
            SendTheme();
            SendContext();
        };
        Content = View;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RhinoDoc.SelectObjects += OnSelectionChanged;
        RhinoDoc.DeselectObjects += OnSelectionChanged;
        RhinoDoc.DeselectAllObjects += OnSelectionChanged;
        // Rhino's own theme/colour settings. NOT RhinoApp.AppSettingsChanged: that is what *triggers*
        // ThemeSettings to recompute, so a handler on it can run first and read the previous colours,
        // which is what left some of the panel on the old theme after a switch.
        // Rhino's own theme and colour settings. NOT RhinoApp.AppSettingsChanged: that is what
        // *triggers* ThemeSettings to recompute, so a handler on it can run first and read the
        // previous colours, which is what left parts of the panel on the old theme after a switch.
        Rhino.UI.ThemeSettings.ThemeChanged += OnThemeChanged;
        HookOsAppearance();
        LoadPage();
    }

    // Rhino 9's Eto raises Application.ThemeChanged when the operating system appearance changes,
    // which Rhino's own settings signal does not necessarily cover. The Eto that RhinoCommon compiles
    // against has not caught up, so it is bound at runtime: present on Rhino 9, absent on Rhino 8,
    // where the panel simply follows Rhino's own theme setting instead.
    private EventInfo? OsAppearanceEvent { get; set; }
    private Delegate? OsAppearanceHandler { get; set; }

    private void HookOsAppearance()
    {
        try
        {
            if (Application.Instance is not { } app)
                return;
            if (typeof(Application).GetEvent("ThemeChanged") is not { EventHandlerType: { } type } info)
                return;

            Delegate handler = Delegate.CreateDelegate(type, this, nameof(OnThemeChanged));
            info.AddEventHandler(app, handler);
            OsAppearanceEvent = info;
            OsAppearanceHandler = handler;
        }
        catch (Exception ex) when (ex is ArgumentException or MissingMethodException or InvalidOperationException)
        {
            // Shaped differently than expected. Rhino's own theme signal still covers the common case.
        }
    }

    private void UnhookOsAppearance()
    {
        if (OsAppearanceEvent is null || OsAppearanceHandler is null || Application.Instance is not { } app)
            return;
        try
        {
            OsAppearanceEvent.RemoveEventHandler(app, OsAppearanceHandler);
        }
        catch (InvalidOperationException)
        {
        }
        OsAppearanceEvent = null;
        OsAppearanceHandler = null;
    }

    // Switching theme changes every colour the panel took from Rhino's paint palette.
    //
    // Deliberately NOT read here. ThemeSettings raises this after nulling its zones, so reading now
    // rebuilds them, and a zone's defaults key off ThemeZone.IsDark -> HostUtils.RunningInDarkMode
    // -> AdvancedSettings.DarkMode. That flag is not necessarily flipped yet, so an immediate read
    // rebuilds the palette from the theme being replaced and the panel ends up one switch behind.
    //
    // Idle runs once Rhino has finished applying the change. It is sampled twice because there is no
    // documented point at which the flag is guaranteed settled; the fingerprint in SendTheme makes
    // the second sample free when the first was already right.
    private void OnThemeChanged(object? sender, EventArgs e) => QueueThemeRefresh(samples: 2);

    private int PendingThemeSamples { get; set; }

    private void QueueThemeRefresh(int samples)
    {
        bool alreadyQueued = PendingThemeSamples > 0;
        PendingThemeSamples = Math.Max(PendingThemeSamples, samples);
        if (!alreadyQueued)
            RhinoApp.Idle += OnIdleRefreshTheme;
    }

    private void OnIdleRefreshTheme(object? sender, EventArgs e)
    {
        if (--PendingThemeSamples <= 0)
        {
            PendingThemeSamples = 0;
            RhinoApp.Idle -= OnIdleRefreshTheme;
        }

        if (Loaded)
            SendTheme();
    }

    // Selection events are per-document and fire on the UI thread, so this only has to filter for
    // its own document before re-reading.
    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (Loaded && TryDoc(out RhinoDoc doc) && doc.RuntimeSerialNumber == DocSerial)
            SendContext();
    }

    // Also the Reload menu item. The page is served from an embedded resource rather than a URL, so
    // there is nothing for the document to reload itself: re-loading the HTML is the only way, which
    // is why the webview's own Reload never did anything. The panel re-handshakes on load, so the
    // conversation comes straight back.
    private void LoadPage()
    {
        Bridge.Reset();
        // The page about to load knows no tokens, so the next send must not be suppressed.
        LastTheme = string.Empty;
        Assembly assembly = typeof(AIPAnel).Assembly;
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
        RhinoDoc.SelectObjects -= OnSelectionChanged;
        RhinoDoc.DeselectObjects -= OnSelectionChanged;
        RhinoDoc.DeselectAllObjects -= OnSelectionChanged;
        // Static/application-lifetime events: not detaching would keep this panel alive for as long
        // as Rhino runs.
        Rhino.UI.ThemeSettings.ThemeChanged -= OnThemeChanged;
        if (PendingThemeSamples > 0)
        {
            RhinoApp.Idle -= OnIdleRefreshTheme;
            PendingThemeSamples = 0;
        }
        UnhookOsAppearance();
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

            case LoadConversationCommand load:
                LoadConversation(load.SessionId);
                break;

            case ResumeConversationCommand resume:
                ResumeConversation(resume.SessionId);
                break;

            case ExitReviewCommand:
                ExitReview();
                break;

            case SelectAgentCommand select:
                SelectAgent(select.Name);
                break;

            case AnswerQuestionCommand answer:
                Answer(answer.Items);
                break;

            case DismissQuestionCommand dismiss:
                Dismiss(dismiss.Ids);
                break;

            case ToolChipCommand chip:
                RunToolChip(chip);
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

            case OpenMenuCommand menu:
                PanelMenu.Show(View, menu, Bridge.Post, LoadPage);
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
        Persist();

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
        SendHistory();
    }

    // ------------------------------------------------------------------ history

    private void SendHistory()
    {
        List<PanelHistoryEntry> entries = new();
        foreach (ConversationDto convo in ConversationStore.List())
        {
            // Filtered on read as well as on write: the store already holds empty conversations
            // saved before the panel started skipping them, and there is nothing to review in one.
            if (convo.Turns.Count == 0)
                continue;

            TokenUsage total = TokenUsage.Empty;
            foreach (TurnDto turn in convo.Turns)
                total += turn.Usage;

            entries.Add(new PanelHistoryEntry(
                convo.SessionId,
                Title(convo),
                PrettyName.Of(convo.AgentName),
                convo.DocTitle,
                convo.StartedAt.ToString("O"),
                convo.Turns.Count,
                new PanelUsage(total.InputTokens, total.OutputTokens, total.CostUsd),
                Resumable(convo.AgentName)));
        }
        Bridge.Post(new HistoryEvent(entries));
    }

    private static string Title(ConversationDto convo)
    {
        string prompt = convo.Turns.Count > 0 ? convo.Turns[0].Prompt : string.Empty;
        string line = prompt.Split('\n').FirstOrDefault(static l => l.Trim().Length > 0)?.Trim() ?? string.Empty;
        if (line.Length == 0)
            return "(no prompt)";
        return line.Length > 80 ? line[..80].TrimEnd() + "…" : line;
    }

    // Matches the agent picker's drivability check: a registered but disabled or missing agent has
    // no launchable runner, so the panel must offer review without a Resume.
    private static bool Resumable(string agentName) =>
        AgentRegistry.Chain.Any(r => r.Definition.Name == agentName && r is { Definition.Enabled: true, Available: true });

    // Read-only: the saved transcript is restored into a detached Conversation and replayed through
    // the same feed the live path uses, so there is no second rendering path to keep in step.
    private void LoadConversation(string sessionId)
    {
        if (!ConversationStore.TryLoad(sessionId, out ConversationDto dto))
        {
            Bridge.Post(new NoticeEvent("error", "That conversation could not be loaded."));
            return;
        }

        Unsubscribe();
        Review = new ConversationFeed(Conversation.Restore(dto), Bridge.Post);
        Review.Replay(readOnly: true);
    }

    private void ExitReview()
    {
        Review = null;
        Resubscribe();
        Feed?.Replay();
    }

    private void ResumeConversation(string sessionId)
    {
        if (!TryDoc(out RhinoDoc doc) || !ConversationStore.TryLoad(sessionId, out ConversationDto dto))
            return;

        // Resuming disposes the doc's pooled runner. Doing that mid-turn would kill the streaming
        // process and silently abandon the in-flight answer, so refuse rather than abort it.
        if (TurnRunning())
        {
            Bridge.Post(new NoticeEvent("warn", "Stop the running turn before resuming another conversation."));
            return;
        }

        Persist();

        if (!AgentHost.TryResume(doc, dto, out IAgentRunner _))
        {
            Bridge.Post(new NoticeEvent("error", $"Cannot resume: agent '{PrettyName.Of(dto.AgentName)}' is no longer available."));
            return;
        }

        LastAgentKey = dto.AgentName;
        Review = null;
        Unsubscribe();
        Resubscribe();
        Feed?.Replay();
        SendAgents();
        SendHistory();
    }

    // Turns are already saved as they complete; this catches the final state when the user moves
    // away. An untouched conversation is skipped, or every New would leave an empty row in the
    // history the user then has to read past.
    private void Persist()
    {
        if (TryConversation(out Conversation current) && current.Turns.Count > 0)
            PersistConversationHook(current);
    }

    private bool TurnRunning()
    {
        if (!TryConversation(out Conversation convo))
            return false;
        IReadOnlyList<Turn> turns = convo.Turns;
        return turns.Count > 0 && !turns[^1].Completed;
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

    // One submit for every question showing: the panel sends the whole set, so this composes a single
    // reply and wakes the agent once rather than once per card.
    private void Answer(IReadOnlyList<QuestionAnswer> items)
    {
        List<string> ids = [];
        foreach (QuestionAnswer item in items)
            ids.Add(item.Id);

        if (Feed is null || !Feed.TryResolveQuestions(ids, out IReadOnlyList<PendingQuestion> questions) || !TryDoc(out RhinoDoc doc))
            return;

        List<string> answers = [];
        bool anyPicked = false;
        foreach (QuestionAnswer item in items)
        {
            if (item.Answers.Count > 0) anyPicked = true;
            answers.Add(string.Join(", ", item.Answers));
        }

        if (!anyPicked)
        {
            Dismiss(ids);
            return;
        }

        // First-wins through the same claim the command-line picker uses: losing it means the picker
        // already dispatched these questions, so there is nothing left to do but re-render.
        if (AskUserPicker.TryClaim(doc.RuntimeSerialNumber, questions))
            AgentDispatch.AnswerActive(doc, UserMessage.FromText(AskUserPicker.Compose(questions, answers)));

        if (TryConversation(out Conversation convo))
            convo.ClearPendingQuestions(questions);
    }

    // A stale card must not cancel a command the user started by hand, so the call has to still be running.
    private void RunToolChip(ToolChipCommand chip)
    {
        if (Review is not null || Feed is null || !Feed.IsCallRunning(chip.CallId) || !TryDoc(out RhinoDoc doc))
            return;

        switch (chip.ChipId)
        {
            case ToolChips.CancelId:
                RhinoApp.RunScript(doc.RuntimeSerialNumber, "!_Cancel", false);
                break;
        }
    }

    private void Dismiss(IReadOnlyList<string> ids)
    {
        if (Feed is null || !Feed.TryResolveQuestions(ids, out IReadOnlyList<PendingQuestion> questions))
            return;

        if (TryDoc(out RhinoDoc doc))
            AskUserPicker.Cancel(doc.RuntimeSerialNumber, questions);
        if (TryConversation(out Conversation convo))
            convo.ClearPendingQuestions(questions);
    }

    private void OpenSettings()
    {
        AISettingsDialog dialog = new();
        dialog.ShowModal(this);
        AgentRegistry.Refresh();
        // Forced: this runs on the panel's `ready`, so the page is new and has nothing yet.
        SendTheme(force: true);
        SendAgents();
        SendContext();
        SendHistory();
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

        // Forced: this runs on the panel's `ready`, so the page is new and has nothing yet.
        SendTheme(force: true);
        SendAgents();
        SendContext();
        SendHistory();
    }

    // The @ menu is only as good as this, and the selection changes constantly, so it is re-read on
    // every selection event rather than cached.
    private void SendContext()
    {
        if (TryDoc(out RhinoDoc doc))
            Bridge.Post(new ContextEvent(PanelContextSource.For(doc)));
    }

    // Rhino restyles Eto, so its SystemColors are the panel chrome we want to match. There is no
    // theme-changed event to hook, so this re-runs whenever the panel is shown, which is what
    // happens after a trip through Options to change the theme.
    // force is not optional politeness: the fingerprint below suppresses an unchanged theme, and a
    // freshly loaded page has no tokens at all, so a suppressed send leaves it falling back to the
    // stylesheet's prefers-color-scheme guess. On a machine whose OS theme differs from Rhino's, that
    // renders the whole panel in the opposite theme.
    private void SendTheme(bool force = false)
    {
        PanelTheme.Rgb Read(Eto.Drawing.Color c) => new(c.R, c.G, c.B);
        PanelTheme.Rgb Convert(System.Drawing.Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
        PanelTheme.Rgb Paint(Rhino.ApplicationSettings.PaintColor which) =>
            Convert(Rhino.ApplicationSettings.AppearanceSettings.GetPaintColor(which));

        // Rhino's own paint palette, which is exactly what it paints panels with: Panels.SetBackColor
        // assigns the host's BackColor from GetPaintColor(PaintColor.PanelBackground). Eto's
        // SystemColors cannot answer this, because its handlers disagree across platforms about which
        // constant carries the panel tone, and neither of them is Rhino's themed value anyway.
        //
        // Only the four Rhino has no equivalent for still come from Eto.
        bool windows = !Environment.OSVersion.Platform.Equals(PlatformID.Unix);
        PanelTheme.Palette palette = new(
            Chrome: Paint(Rhino.ApplicationSettings.PaintColor.PanelBackground),
            Field: Paint(Rhino.ApplicationSettings.PaintColor.EditBoxBackground),
            Text: Paint(Rhino.ApplicationSettings.PaintColor.TextEnabled),
            Dim: Paint(Rhino.ApplicationSettings.PaintColor.TextDisabled),
            Border: Paint(Rhino.ApplicationSettings.PaintColor.GridLinesOnPanelBackground),
            Accent: Read(Eto.Drawing.SystemColors.Highlight),
            AccentText: Read(Eto.Drawing.SystemColors.HighlightText),
            // Rhino's own hyperlink colour. Eto's LinkText is no use here: its Windows handler maps
            // it to the selection highlight.
            Link: Convert(Rhino.ApplicationSettings.AppearanceSettings.CommandPromptHypertextColor),
            Selection: Read(Eto.Drawing.SystemColors.Selection),
            SelectionText: Read(Eto.Drawing.SystemColors.SelectionText));

        Dictionary<string, string> tokens = PanelTheme.Tokens(palette);

        // Rhino themes Eto, so its default UI font is the one every other Rhino panel uses.
        Eto.Drawing.Font font = Eto.Drawing.SystemFonts.Default();
        foreach (KeyValuePair<string, string> entry in PanelTheme.Fonts(font.FamilyName, font.Size, windows))
            tokens[entry.Key] = entry.Value;

        // AppSettingsChanged fires for every settings change, and repeatedly while a colour picker
        // is dragged, so an unchanged theme is not worth a round trip into the page.
        // From the resolved ground, not the raw palette entry, so the scheme always agrees with
        // what was actually rendered.
        string scheme = PanelTheme.IsDarkTheme(palette) ? "dark" : "light";
        string fingerprint = scheme + string.Join(";", tokens.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value}"));
        if (!force && fingerprint == LastTheme)
            return;
        LastTheme = fingerprint;

        Bridge.Post(new ThemeEvent(scheme, tokens));
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
        if (!TryConversation(out Conversation convo))
        {
            Unsubscribe();
            return;
        }

        // Already attached to this exact conversation: keep the feed. Replacing it would reset its
        // high-water marks, and the next Changed would re-emit the whole transcript as new events on
        // top of what the panel already has. Send() resubscribes before every prompt, so this is the
        // difference between one turn appearing and every previous turn appearing again.
        if (ReferenceEquals(Subscribed, convo) && Feed is not null)
            return;

        Unsubscribe();

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
        if (Loaded && Review is null)
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
}
