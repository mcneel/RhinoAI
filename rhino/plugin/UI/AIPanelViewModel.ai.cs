using System.IO;
using System.Net;
using System.Threading.Tasks;
using Rhino.UI;

using Eto.Forms;

using Rhino.Runtime;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI;

internal partial class AIPanelViewModel : IDisposable
{
    private const string PageResource = "Rhino.AI.panel.html";

    public Uri PageUrl => new(Listener.Prefixes.First());

    private sealed record ConversationSubscription(Conversation Conversation, ConversationFeed Feed, Action Handler);

    private ConversationSubscription? Subscription { get; set; }

    private ConversationFeed? Feed => Subscription?.Feed;

    private ConversationFeed? Review { get; set; }

    private Guid? PinnedAgentId { get; set; }

    private Conversation? ActiveConversation =>
        Document is { } doc && AgentHost.TryFor(doc, out AgentRunner agent) ? agent.Conversation : null;

    public void Attach()
    {
        RhinoApp.Idle += UpdateSelection;
        RhinoDoc.SelectObjects += OnSelectionChanged;
        RhinoDoc.DeselectObjects += OnSelectionChanged;
        RhinoDoc.DeselectAllObjects += OnSelectionChanged;
        SettingsViewModel.Saved += OnSettingsSaved;
        AgentHost.AttachPanel(DocumentSerialNumber);

        ShowCurrent();
    }

    public void Detach()
    {
        RhinoApp.Idle -= UpdateSelection;
        RhinoDoc.SelectObjects -= OnSelectionChanged;
        RhinoDoc.DeselectObjects -= OnSelectionChanged;
        RhinoDoc.DeselectAllObjects -= OnSelectionChanged;
        SettingsViewModel.Saved -= OnSettingsSaved;
        AgentHost.DetachPanel(DocumentSerialNumber);

        Unsubscribe();
    }

    public void Dispose()
    {
        Detach();
        Listener.Close();
    }

    private bool UpdatedSelected { get; set; } = false;

    private void UpdateSelection(object? sender, EventArgs e)
    {
        if (!UpdatedSelected) return;
        UpdatedSelected = false;
        SendContext();
    }
    
    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (ChangedDocument(e)?.RuntimeSerialNumber == DocumentSerialNumber)
        {
            UpdatedSelected = true;            
        }
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
            AnswerPermissionCommand permission => AnswerPermission(permission.Id, permission.Answer),
            ToolChipCommand chip => RunToolChip(chip),
            PickAttachmentsCommand => PickAttachments(),
            OpenImageCommand open => OpenImage(open.Id),
            SaveImageCommand save => SaveImage(save.Id),
            SetZoomCommand zoom => SetZoom(zoom.Level),
            OpenSettingsCommand => OpenSettings(),
            OpenUrlCommand open => OpenUrl(open.Url),
            ClipboardCommand copy => CopyToClipboard(copy.Text),
            OpenMenuCommand menu => ShowMenu(menu),
            ListMcpsCommand => SendMcps(),
            ToggleMcpCommand toggle => ToggleMcp(toggle.Name, toggle.Enabled),

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
                Bridge.Post(new NoticeEvent("error", string.Format(Localization.LocalizeString("Could not attach {0}.", 3), sent.Name)));
        }

        if ((text.Length == 0 && attachments.Count == 0) || Document is not { } doc)
            return false;

        if (!AgentHost.TryFor(doc, out AgentRunner _))
        {
            Bridge.Post(new NoticeEvent("error", Localization.LocalizeString("No AI agent available. Open AI settings to configure one.", 4)));
            return false;
        }

        Resubscribe();
        AgentDispatch.PromptActive(doc, new UserMessage(text, attachments));
        return true;
    }

    // RH-100340: a prompt Rhino sends gets its own conversation, so earlier context does not leak into it.
    public bool PromptFromHost(string text)
    {
        if (IsTurnRunning)
        {
            Bridge.Post(new NoticeEvent("warn", LOC.STR("Stop the running turn before starting another.")));
            return false;
        }

        if (ActiveConversation is { Turns.Count: > 0 })
            NewConversation();
        else
            ShowLive();

        return Prompt(new PromptRequest(text, []));
    }

    private bool CancelCurrentAgentTurn()
    {
        if (!AgentHost.TryFor(Document, out AgentRunner running)) return false;
        running.Cancel();
        return true;
    }

    private bool NewConversation()
    {
        Persist();

        if (Document is { } doc)
        {
            AgentHost.Stop(doc);
            if (PinnedAgentId is { } pinned)
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
            Bridge.Post(new NoticeEvent("error", Localization.LocalizeString("That conversation could not be loaded.", 5)));
            return false;
        }

        Unsubscribe();
        string agentKey = AgentHost.Find(dto.AgentName)?.Id.ToString() ?? dto.AgentName;
        Review = new ConversationFeed(Conversation.Restore(dto, agentKey), Bridge.Post);
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
            Bridge.Post(new NoticeEvent("warn", Localization.LocalizeString("Stop the running turn before resuming another conversation.", 6)));
            return false;
        }

        Persist();

        if (!AgentHost.TryResume(doc, dto, out AgentRunner resumed))
        {
            Bridge.Post(new NoticeEvent("error", string.Format(Localization.LocalizeString("Cannot resume: agent '{0}' is no longer available.", 7), AgentLabel(dto.AgentName))));
            return false;
        }

        PinnedAgentId = resumed.Definition.Id;
        ShowLive();
        SendAgents();
        SendHistory();
        return true;
    }

    private bool SelectAgent(string key)
    {
        if (Document is not { } doc)
            return false;
        if (AgentHost.Find(key) is not { } picked || !AgentHost.IsUsable(picked))
            return false;

        PinnedAgentId = picked.Id;
        AgentHost.SetActive(doc, picked.Id);
        ShowLive();
        SendAgents();
        return true;
    }

    private bool Login()
    {
        if (!RhinoApp.IsInternetAccessAllowed) return false;

        if (Document is not { } doc)
            return false;
        if (!AgentHost.TryFor(doc, out AgentRunner agent))
        {
            Bridge.Post(new NoticeEvent("error", Localization.LocalizeString("No AI agent available. Open AI settings to configure one.", 8)));
            return false;
        }

        ExitReview();
        agent.Login();
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

    private bool AnswerPermission(string id, PermissionAnswer answer)
    {
        if (Feed is null || !Feed.TryResolvePermission(id, out PendingPermission permission) || permission.IsDecided)
            return false;

        switch (answer)
        {
            case PermissionAnswer.Deny:
                permission.Decide(false);
                break;
            case PermissionAnswer.Allow:
                permission.Decide(true);
                break;
            case PermissionAnswer.Always:
                PermissionPrompt.AllowAlways(permission);
                break;
        }

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
            Eto.Forms.SaveFileDialog dialog = new() { Title = "Save image", FileName = name };
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
        AIInstances.Preferences.ZoomLevel = (int)Math.Round(level * 100);
        return true;
    }

    private static bool OpenSettings()
    {
        Application.Instance.AsyncInvoke(() => RhinoApp.RunScript(AIOptionsPage.OpenScript, false));
        return true;
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        SendAgents();
        SendContext();
        SendHistory();
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

#if NETCOREAPP
    private const bool PluginCommandsAvailable = true;
#else
    private const bool PluginCommandsAvailable = false;
#endif

    private void SendEnvironment()
    {
        Bridge.Post(new HelloEvent(
            new PanelHost(
                "Rhinoceros",
                RhinoApp.Version.ToString(),
                OperatingSystem.IsWindows() ? "windows" : "macos",
                Document is { } doc ? DocTitle(doc) : "Untitled",
                new PanelCapabilities(Attachments: true, ViewportCapture: true, UndoTurn: false, Grasshopper: true, PluginCommands: PluginCommandsAvailable)),
            PanelStrings.LanguageTag(),
            PanelStrings.Localized()));

        Bridge.Post(new ZoomEvent("set", AIInstances.Preferences.ZoomLevel / 100.0));

        SendAgents();
        SendContext();
        SendHistory();
    }

    private void SendContext()
    {
        if (Document is null) return;
        Bridge.Post(new ContextEvent(PanelContextSource.For(Document)));
    }

    private void SendAgents()
    {
        List<PanelAgent> agents = new();
        foreach (IAgentDefinition definition in AgentHost.Definitions)
        {
            bool enabled = definition.Enabled && RhinoApp.IsInternetAccessAllowed;
            string availability = !enabled ? "disabled" : !definition.Available ? "missing" : "ready";
            string model = definition.CurrentModelId is { Length: > 0 } current ? current
                : definition.DefaultModelId is { Length: > 0 } fallback ? fallback
                : "default";
            agents.Add(new PanelAgent(
                definition.Id.ToString(),
                definition.DisplayName,
                model,
                definition.AvailableModels.FirstOrDefault(m => m.ModelId == model)?.DisplayName ?? model,
                availability,
                null));
        }

        IAgentDefinition? active = Document is { } doc && AgentHost.TryFor(doc, out AgentRunner agent)
            ? agent.Definition
            : AgentHost.Definitions.FirstOrDefault(AgentHost.IsUsable);

        if (active is not null)
            PinnedAgentId = active.Id;

        Bridge.Post(new AgentsEvent(agents, active?.Id.ToString()));
    }

    private bool SendMcps()
    {
        List<PanelMcp> mcps = new();
        if (Document is { } doc && AgentHost.TryFor(doc, out AgentRunner runner))
        {
            foreach (Mcp mcp in runner.Mcps)
                mcps.Add(new PanelMcp(mcp.Name, McpKind(mcp), mcp.Tools.Count, mcp.Enabled, CanToggle: true));

            // The router is only created by the first prompt, so until then there is no instance to toggle.
            if (!runner.HasAttachedMcps && !mcps.Any(m => string.Equals(m.Name, RouterMcpConfig.ServerName, StringComparison.OrdinalIgnoreCase)))
                mcps.Add(new PanelMcp(RouterMcpConfig.ServerName, "http", 0, Enabled: true, CanToggle: false));
        }

        Bridge.Post(new McpsEvent(mcps));
        return true;
    }

    private bool ToggleMcp(string name, bool enabled)
    {
        if (Document is not { } doc || !AgentHost.TryFor(doc, out AgentRunner runner))
            return false;

        if (runner.Mcps.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) is { } mcp)
            mcp.Enabled = enabled;

        return SendMcps();
    }

    private static string McpKind(Mcp mcp) => mcp switch
    {
        StdioMcp => "stdio",
        HttpMcp => "http",
        _ => "memory",
    };

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
                AgentLabel(convo.AgentName),
                convo.DocTitle,
                convo.StartedAt.ToString("O"),
                convo.Turns.Count,
                new PanelUsage(total.InputTokens, total.OutputTokens, total.CostUsd),
                AgentHost.Find(convo.AgentName) is { } definition && AgentHost.IsUsable(definition)));
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

    private static string AgentLabel(string agentKey) =>
        AgentHost.Find(agentKey)?.DisplayName ?? agentKey;

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
        Subscription = new ConversationSubscription(convo, new ConversationFeed(convo, Bridge.Post), handler);
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
