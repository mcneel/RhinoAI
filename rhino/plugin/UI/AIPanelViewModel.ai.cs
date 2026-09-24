using System.IO;
using System.Net;
using System.Threading.Tasks;

using Eto.Forms;

using Rhino.Runtime;

namespace Rhino.AI.UI;

internal partial class AIPanelViewModel : IDisposable
{
    private const string PageResource = "Rhino.AI.panel.html";

    public Uri PageUrl => new(Listener.Prefixes.First());

    private sealed record ConversationSubscription(Conversation Conversation, ConversationFeed Feed, Action Handler);

    private ConversationSubscription? Subscription { get; set; }

    private ConversationFeed? Feed => Subscription?.Feed;

    private ConversationFeed? Review { get; set; }

    private string? PinnedAgentName { get; set; }

    private Conversation? ActiveConversation =>
        Document is { } doc && AgentHost.TryFor(doc, out IAgentRunner agent) ? agent.Conversation : null;

    public void Attach()
    {
        RhinoDoc.SelectObjects += OnSelectionChanged;
        RhinoDoc.DeselectObjects += OnSelectionChanged;
        RhinoDoc.DeselectAllObjects += OnSelectionChanged;

        ShowCurrent();
    }

    public void Detach()
    {
        RhinoDoc.SelectObjects -= OnSelectionChanged;
        RhinoDoc.DeselectObjects -= OnSelectionChanged;
        RhinoDoc.DeselectAllObjects -= OnSelectionChanged;
        Unsubscribe();
    }

    public void Dispose()
    {
        Detach();
        Listener.Close();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (ChangedDocument(e)?.RuntimeSerialNumber == DocumentSerialNumber)
            SendContext();
    }

    private static RhinoDoc? ChangedDocument(EventArgs e) => e switch
    {
        DocObjects.RhinoObjectSelectionEventArgs selection => selection.Document,
        DocObjects.RhinoDeselectAllObjectsEventArgs deselect => deselect.Document,
        _ => null,
    };

#region PAGE SERVER

    private void StartPageServer()
    {
        byte[] page = ReadPage();

        try
        {
            Listener.Start();
        }
        catch (HttpListenerException ex)
        {
            HostUtils.LogDebugEvent($"[rhino-ai] the AI panel could not start its page server: {ex.Message}.\n");
            return;
        }

        _ = ServePageAsync(page);
    }

    private static byte[] ReadPage()
    {
        using Stream? resource = typeof(AIPanelViewModel).Assembly.GetManifestResourceStream(PageResource);
        if (resource is null)
            throw new InvalidOperationException($"The panel page '{PageResource}' is missing from the plug-in.");

        using MemoryStream buffer = new();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }

    private async Task ServePageAsync(byte[] page)
    {
        while (Listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await Listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            await RespondAsync(context, page).ConfigureAwait(false);
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, byte[] page)
    {
        HttpListenerResponse response = context.Response;
        string route = context.Request.Url?.AbsolutePath ?? string.Empty;
        try
        {
            if (route is "/")
            {
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = page.Length;
                await response.OutputStream.WriteAsync(page, 0, page.Length).ConfigureAwait(false);
            }
            else if (route.StartsWith(ServedImages.Route, StringComparison.Ordinal)
                && ServedImages.Resolve(route.Substring(ServedImages.Route.Length)) is { } file)
            {
                response.ContentType = ServedImages.MediaType(file);
                using FileStream bytes = File.OpenRead(file);
                response.ContentLength64 = bytes.Length;
                await bytes.CopyToAsync(response.OutputStream).ConfigureAwait(false);
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            HostUtils.LogDebugEvent($"[rhino-ai] the AI panel could not serve its page: {ex.Message}.\n");
        }
        finally
        {
            response.Close();
        }
    }

#endregion

#region FROM THE PANEL

    private void IncomingCommand(PanelCommand? command)
    {
        _ = command switch
        {
            ReadyCommand => Ready(),
            PromptCommand prompt => Prompt(prompt.Request),
            CancelCommand => CancelCurrentAgentTurn(),
            NewConversationCommand => NewConversation(),
            LoadConversationCommand load => LoadConversation(load.SessionId),
            ResumeConversationCommand resume => ResumeConversation(resume.SessionId),
            ExitReviewCommand => ExitReview(),
            SelectAgentCommand select => SelectAgent(select.Name),
            LoginCommand => Login(),
            AnswerQuestionCommand answer => Answer(answer.Items),
            DismissQuestionCommand dismiss => Dismiss(dismiss.Ids),
            ToolChipCommand chip => RunToolChip(chip),
            PickAttachmentsCommand => PickAttachments(),
            OpenImageCommand open => OpenImage(open.Id),
            SaveImageCommand save => SaveImage(save.Id),
            SetZoomCommand zoom => SetZoom(zoom.Level),
            OpenSettingsCommand => OpenSettings(),
            OpenUrlCommand open => OpenUrl(open.Url),
            ClipboardCommand copy => CopyToClipboard(copy.Text),
            OpenMenuCommand menu => ShowMenu(menu),

            _ => false
        };
    }

    private bool Ready()
    {
        SendEnvironment();
        ShowCurrent();
        return true;
    }

    private bool Prompt(PromptRequest request)
    {
        string text = request.Text.Trim();
        List<Attachment> attachments = new(request.Attachments.Count);
        foreach (PanelAttachment sent in request.Attachments)
        {
            if (sent.ToAttachment() is { } attachment)
                attachments.Add(attachment);
            else
                Bridge.Post(new NoticeEvent("error", string.Format(Rhino.UI.LOC.STR("Could not attach {0}."), sent.Name)));
        }

        if ((text.Length == 0 && attachments.Count == 0) || Document is not { } doc)
            return false;

        if (!AgentHost.TryFor(doc, out IAgentRunner _))
        {
            Bridge.Post(new NoticeEvent("error", Rhino.UI.LOC.STR("No AI agent available. Open AI settings to configure one.")));
            return false;
        }

        Resubscribe();
        AgentDispatch.PromptActive(doc, new UserMessage(text, attachments));
        return true;
    }

    private bool CancelCurrentAgentTurn()
    {
        if (!AgentHost.TryFor(Document, out IAgentRunner running)) return false;
        running.Cancel();
        return true;
    }

    private bool NewConversation()
    {
        Persist();

        if (Document is { } doc)
        {
            AgentHost.Stop(doc);
            if (PinnedAgentName is { } pinned)
                AgentHost.SetActive(doc, pinned);
        }

        ShowLive();
        SendHistory();
        return true;
    }

    private bool LoadConversation(string sessionId)
    {
        if (!ConversationStore.TryLoad(sessionId, out ConversationDto dto))
        {
            Bridge.Post(new NoticeEvent("error", Rhino.UI.LOC.STR("That conversation could not be loaded.")));
            return false;
        }

        Unsubscribe();
        Review = new ConversationFeed(Conversation.Restore(dto), Bridge.Post, CodexHome.GeneratedImages);
        Review.Replay(readOnly: true);
        return true;
    }

    private bool ExitReview()
    {
        ShowLive();
        return true;
    }

    private bool ResumeConversation(string sessionId)
    {
        if (Document is not { } doc || !ConversationStore.TryLoad(sessionId, out ConversationDto dto))
            return false;

        if (IsTurnRunning)
        {
            Bridge.Post(new NoticeEvent("warn", Rhino.UI.LOC.STR("Stop the running turn before resuming another conversation.")));
            return false;
        }

        Persist();

        if (!AgentHost.TryResume(doc, dto, out IAgentRunner _))
        {
            Bridge.Post(new NoticeEvent("error", string.Format(Rhino.UI.LOC.STR("Cannot resume: agent '{0}' is no longer available."), dto.AgentName)));
            return false;
        }

        PinnedAgentName = dto.AgentName;
        ShowLive();
        SendAgents();
        SendHistory();
        return true;
    }

    private bool SelectAgent(string name)
    {
        if (Document is not { } doc)
            return false;
        if (AgentRegistry.Instance.AllDefinitions.FirstOrDefault(r => r.Name == name) is not { Available: true } picked
            || !AISettings.IsEnabled(picked))
            return false;

        PinnedAgentName = name;
        AgentHost.SetActive(doc, name);
        ShowLive();
        SendAgents();
        return true;
    }

    private bool Login()
    {
        if (!RhinoApp.IsInternetAccessAllowed) return false;
        
        if (Document is not { } doc)
            return false;
        if (!AgentHost.TryFor(doc, out IAgentRunner agent))
        {
            Bridge.Post(new NoticeEvent("error", Rhino.UI.LOC.STR("No AI agent available. Open AI settings to configure one.")));
            return false;
        }
        if (!AgentDispatch.TryEnsureListener(doc, out int port))
        {
            Bridge.Post(new NoticeEvent("error", Rhino.UI.LOC.STR("Could not start an MCP server for this document.")));
            return false;
        }

        ExitReview();
        string workingDirectory = !string.IsNullOrEmpty(doc.Path)
            ? Path.GetDirectoryName(doc.Path) ?? Path.GetTempPath()
            : Path.GetTempPath();
        _ = agent.LoginAsync($"http://localhost:{port}/agent", workingDirectory);
        return true;
    }

    private bool Answer(IReadOnlyList<QuestionAnswer> items)
    {
        List<string> ids = [];
        foreach (QuestionAnswer item in items)
            ids.Add(item.Id);

        if (Feed is null || !Feed.TryResolveQuestions(ids, out IReadOnlyList<PendingQuestion> questions) || Document is not { } doc)
            return false;

        List<string> answers = [];
        bool anyPicked = false;
        foreach (QuestionAnswer item in items)
        {
            if (item.Answers.Count > 0) anyPicked = true;
            answers.Add(string.Join(", ", item.Answers));
        }

        if (!anyPicked)
            return Dismiss(ids);

        AgentDispatch.AnswerActive(doc, UserMessage.FromText(QuestionReply.Compose(questions, answers)));
        ActiveConversation?.ClearPendingQuestions(questions);
        return true;
    }

    private bool Dismiss(IReadOnlyList<string> ids)
    {
        if (Feed is null || !Feed.TryResolveQuestions(ids, out IReadOnlyList<PendingQuestion> questions))
            return false;

        ActiveConversation?.ClearPendingQuestions(questions);
        return true;
    }

    private bool RunToolChip(ToolChipCommand chip)
    {
        if (Review is not null || Feed is null || !Feed.IsCallRunning(chip.CallId) || Document is not { } doc)
            return false;

        return chip.ChipId switch
        {
            ToolChips.CancelId => RhinoApp.RunScript(doc.RuntimeSerialNumber, "!_Cancel", false),
            _ => false,
        };
    }

    private bool PickAttachments()
    {
        Application.Instance.AsyncInvoke(() =>
        {
            IReadOnlyList<PanelAttachment> picked =
                AttachmentPicker.Pick(View, problem => Bridge.Post(new NoticeEvent("error", problem)));
            if (picked.Count > 0)
                Bridge.Post(new AttachmentsAddEvent(picked));
        });
        return true;
    }

    private static bool OpenImage(string id)
    {
        if (ServedImages.Resolve(id) is not { } path)
            return false;

        Application.Instance.Open(AsLink(path));
        return true;
    }

    private bool SaveImage(string id)
    {
        if (ServedImages.Resolve(id) is not { } path)
            return false;

        Application.Instance.AsyncInvoke(() =>
        {
            string name = Path.GetFileName(path);
            SaveFileDialog dialog = new() { Title = "Save image", FileName = name };
            dialog.Filters.Add(new FileFilter(Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), Path.GetExtension(path)));

            if (dialog.ShowDialog(View) != DialogResult.Ok)
                return;

            try
            {
                File.Copy(path, dialog.FileName, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                Bridge.Post(new NoticeEvent("error", $"Could not save {name}: {ex.Message}"));
            }
        });
        return true;
    }

    private static bool SetZoom(double level)
    {
        AISettings.ZoomLevel = (int)Math.Round(level * 100);
        return true;
    }

    private bool OpenSettings()
    {
        AISettingsDialog dialog = new();
        dialog.ShowModal(View);
        SendAgents();
        SendContext();
        SendHistory();
        return true;
    }

    private static bool OpenUrl(string url)
    {
        Application.Instance.Open(AsLink(url));
        return true;
    }

    // A bare local path is not a URL and only the Windows shell will take one, so both platforms are handed a file URI.
    private static string AsLink(string url)
    {
        if (!Path.IsPathRooted(url))
            return url;

        try
        {
            return new Uri(url).AbsoluteUri;
        }
        catch (UriFormatException)
        {
            return url;
        }
    }

    private static bool CopyToClipboard(string text)
    {
        Clipboard.Instance.Text = text;
        return true;
    }

    private bool ShowMenu(OpenMenuCommand menu)
    {
        PanelMenu.Show(View, menu, Bridge.Post, View.Reload);
        return true;
    }

#endregion

#region TO THE PANEL

    private void SendEnvironment()
    {
        Bridge.Post(new HelloEvent(
            new PanelHost(
                "Rhinoceros",
                RhinoApp.Version.ToString(),
                OperatingSystem.IsWindows() ? "windows" : "macos",
                Document is { } doc ? DocTitle(doc) : "Untitled",
                new PanelCapabilities(Attachments: true, ViewportCapture: true, UndoTurn: false, Grasshopper: true)),
            PanelStrings.LanguageTag(),
            PanelStrings.Localized()));

        Bridge.Post(new ZoomEvent("set", AISettings.ZoomLevel / 100.0));

        SendAgents();
        SendContext();
        SendHistory();
    }

    private void SendContext()
    {
        if (Document is { } doc)
            Bridge.Post(new ContextEvent(PanelContextSource.For(doc)));
    }

    private void SendAgents()
    {
        List<PanelAgent> agents = new();
        foreach (AgentDefinition definition in AgentRegistry.Instance.AllDefinitions)
        {
            bool enabled = AISettings.IsEnabled(definition);
            string availability = !enabled ? "disabled" : !definition.Available ? "missing" : "ready";
            string model = AISettings.EffectiveModel(definition) is { Length: > 0 } chosen ? chosen : "default";
            agents.Add(new PanelAgent(
                definition.Name,
                definition.Name,
                model,
                definition.Models.FirstOrDefault(spec => spec.Id == model)?.Display ?? model,
                availability,
                availability switch
                {
                    "disabled" => "turned off in AI settings",
                    "missing" => $"'{definition.Name}' was not found",
                    _ => null,
                }));
        }

        string? active = Document is { } doc && AgentHost.TryFor(doc, out IAgentRunner agent)
            ? agent.Name
            : AgentRegistry.Instance.AllDefinitions.FirstOrDefault(r => r.Available && AISettings.IsEnabled(r))?.Name;

        if (active is { Length: > 0 })
            PinnedAgentName = active;

        Bridge.Post(new AgentsEvent(agents, active));
    }

    private void SendHistory()
    {
        List<PanelHistoryEntry> entries = new();
        foreach (ConversationDto convo in ConversationStore.List())
        {
            if (convo.Turns.Count == 0)
                continue;

            TokenUsage total = TokenUsage.Empty;
            foreach (TurnDto turn in convo.Turns)
                total += turn.Usage;

            entries.Add(new PanelHistoryEntry(
                convo.SessionId,
                Title(convo),
                convo.AgentName,
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
        return line.Length > 80 ? line.Substring(0, 80).TrimEnd() + "…" : line;
    }

    private static bool Resumable(string agentName) =>
        AgentRegistry.Instance.AllDefinitions.Any(r => r.Name == agentName && r.Available && AISettings.IsEnabled(r));

    private static string DocTitle(RhinoDoc doc) =>
        string.IsNullOrEmpty(doc.Path) ? "Untitled" : Path.GetFileName(doc.Path);

#endregion

#region CONVERSATION

    // A tab change unloads and reloads the panel, and a conversation opened from history is not the live one.
    private void ShowCurrent()
    {
        if (Review is { } review)
        {
            review.Replay(readOnly: true);
            return;
        }

        ShowLive();
    }

    private void ShowLive()
    {
        Review = null;
        Resubscribe();
        Feed?.Replay();
    }

    private void Resubscribe()
    {
        if (ActiveConversation is not { } convo)
        {
            Unsubscribe();
            return;
        }

        // Keeping the feed matters: a replacement resets its high-water marks and re-emits the whole transcript.
        if (Subscription is { } live && ReferenceEquals(live.Conversation, convo))
            return;

        Unsubscribe();

        Action handler = () => RhinoApp.InvokeOnUiThread(new Action(PumpIfLive));
        convo.Changed += handler;
        Subscription = new ConversationSubscription(convo, new ConversationFeed(convo, Bridge.Post, CodexHome.GeneratedImages), handler);
    }

    private void Unsubscribe()
    {
        if (Subscription is { } live)
            live.Conversation.Changed -= live.Handler;
        Subscription = null;
    }

    private void PumpIfLive()
    {
        if (View.Loaded && Review is null)
            Feed?.Pump();
    }

    private void Persist()
    {
        if (ActiveConversation is { Turns.Count: > 0 } current)
            ConversationStore.Save(current);
    }

    private bool IsTurnRunning =>
        ActiveConversation?.Turns is { Count: > 0 } turns && !turns[turns.Count - 1].Completed;

#endregion

}
