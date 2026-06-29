using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using RhinoCommand = Rhino.Commands.Command;

namespace RhMcp;

// One panel instance per document (PanelType.PerDoc). Renders the active agent's Conversation as
// chat bubbles + tool chips, sends prompts through the shared AgentDispatch funnel, and re-renders
// off the Conversation.Changed event marshaled onto the UI thread (the reader loop is off-thread).
[Guid("fb948c98-5987-45a3-8dcb-2814ed77ee3b")]
public class AIPAnel : Panel
{
    public static Guid PanelId => typeof(AIPAnel).GUID;

    private uint DocSerial { get; }

    private DropDown AgentPicker { get; } = new();
    private Label ModelLabel { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
    private DropDown RecentPicker { get; } = new() { ToolTip = "Previous conversations" };
    private TextArea PromptBox { get; } = new() { AcceptsReturn = true, AcceptsTab = false, Height = PromptMinHeight };
    private Button SendButton { get; } = new() { Text = "Send" };

    // Thin indeterminate progress strip pinned flush to the bottom of the chat box (below the
    // transcript, above the input) while a turn runs, so the "thinking" cue reads as part of the
    // conversation area. Collapses to nothing when idle (Visible toggled in SyncSendButton).
    private ProgressBar ThinkingBar { get; } = new() { Indeterminate = true, Visible = false, Height = 6 };

    // In-panel attention cue, shown above the transcript when agent output arrives or a turn finishes
    // while the panel is not the user's focus, cleared the moment focus returns. Rhino's docked panel
    // tab title is fixed at RegisterPanel and has no reliable cross-platform runtime setter, so the
    // unread mark lives inside the content we own rather than on the native tab.
    private Label UnreadBanner { get; } = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Font = SystemFonts.Bold(8),
        Visible = false,
    };
    private StackLayout TranscriptStack { get; } = new()
    {
        Spacing = 8,
        Padding = new Padding(SideMargin, 4),
        HorizontalContentAlignment = HorizontalAlignment.Stretch,   // rows fill width so bubbles can bias left/right
    };
    private StackLayout AttachmentStrip { get; } = new() { Orientation = Orientation.Horizontal, Spacing = 6 };

    private Scrollable TranscriptScroll { get; } = new() { ExpandContentWidth = true };

    // The item rows currently materialized, 1:1 with TranscriptStack.Items[0..Rendered.Count) when
    // Reconcilable. Each row remembers the TranscriptItem it renders so the next render can diff
    // against it and touch only what changed (see Reconcile). MessageBubble/detail Label are tracked
    // for width-pinning: a bubble sizes its own wrapped width/height but must be re-pinned to the
    // viewport on resize, and tool-chip detail labels wrap to the viewport so long JSON never forces
    // a horizontal scroll. LastBudget short-circuits the resize handler when the width budget is
    // unchanged; appended rows are pinned at append time so a stable budget still sizes them.
    private List<RenderedRow> Rendered { get; } = new();

    // Trailing ask_user card, when a question is pending. Sits at TranscriptStack.Items[Rendered.Count];
    // tracked separately because it is not a TranscriptItem and is rebuilt fresh (it captures the live
    // question), so reconciliation manages it as its own slot rather than as a diffed row.
    private Control? QuestionRow { get; set; }

    // True only after a normal live-items render, where TranscriptStack.Items is exactly the Rendered
    // rows plus the optional QuestionRow. The review / no-agent / starter / single-message states put
    // other controls in the stack, so they clear this to force the next live render to reset first.
    private bool Reconcilable { get; set; }

    private Font MeasureFont { get; } = SystemFonts.Default();
    private int LastBudget { get; set; } = -1;

    // A materialized transcript row: the item it renders (for diffing), its top-level Control in the
    // stack, and the bubble / hover meta / detail label it owns (for width-pinning and in-place
    // streaming updates). Bubble + Meta are set for User/Agent rows; Detail for an expandable tool
    // chip; all null otherwise.
    private sealed record RenderedRow(TranscriptItem Item, Control Control, MessageBubble? Bubble, MessageMeta? Meta, Label? Detail);

    private const int SideMargin = 10;        // left/right breathing room around every row
    private const int ScrollbarGuard = 18;    // reserve the vertical scrollbar so content never x-scrolls

    // Auto-grow prompt: the TextArea starts at one line and grows with content up to a cap, beyond
    // which it scrolls internally. The buttons match the min height so they sit flush in the row.
    private const int PromptMinHeight = 34;   // single-line floor; also the attach/send button height
    private const int PromptMaxHeight = 140;  // grows to here, then scrolls inside the box
    private const int PromptInset = 4;         // visual padding wrapped around the borderless TextArea

    // GH2-flavoured one-tap openers shown only on an empty conversation; clicking one sends it.
    private static readonly string[] StarterPrompts =
    [
        "Build a parametric facade in Grasshopper",
        "Add a number slider driving a box",
        "Explain what's on the canvas",
        "Describe the selected objects",
    ];

    private static Dictionary<string, Image?> IconCache { get; } = new();

    private List<Attachment> Pending { get; } = new();

    // Persist a conversation before "New conversation" drops it (turns are already saved on
    // completion; this captures the final state). Overridable so the panel works standalone.
    internal Action<Conversation> PersistConversationHook { get; set; } = ConversationStore.Save;

    // Non-empty while reviewing a persisted transcript: the picker swaps the live view for a
    // read-only render until Back returns to live.
    private ConversationDto? Reviewing { get; set; }

    // Resubscribed on every agent switch; tracked so we can unhook the old one.
    private Conversation? Subscribed { get; set; }
    private Action? SubscribedHandler { get; set; }

    // Suppresses OnAgentPicked while the dropdown is repopulated programmatically, so only a
    // genuine user pick drives SetActive/Resubscribe/Rerender.
    private bool Populating { get; set; }

    // The last agent the user successfully selected; picking a disabled / not-found entry snaps the
    // dropdown back to this since Eto can't disable individual dropdown items.
    private string LastAgentKey { get; set; } = string.Empty;

    // The attention cue rendered above the transcript. Set when agent output or a turn-complete lands
    // while the panel is not focused (see ComputeUnread); cleared back to None on focus.
    private enum UnreadCue
    {
        None,
        Streaming,      // agent output arrived mid-turn while unfocused
        TurnComplete,   // a turn finished while unfocused (supersedes Streaming)
    }

    private UnreadCue Unread { get; set; } = UnreadCue.None;

    // True while any control in the panel holds focus (or the panel was just shown). Drives whether
    // new agent output raises the unread cue. Brief LostFocus/GotFocus flicker as focus moves between
    // child controls only ever re-clears the cue, never falsely raises it.
    private bool PanelFocused { get; set; }

    // Snapshot of the live conversation from the previous render, used to detect transitions: a turn
    // flipping from running to complete, and output arriving while a turn runs.
    private bool WasRunning { get; set; }
    private int LastItemCount { get; set; }

    public AIPAnel()
        : this(Rhino.RhinoDoc.ActiveDoc is { } doc ? doc.RuntimeSerialNumber : 0u)
    {
    }

    public AIPAnel(uint documentSerialNumber)
    {
        DocSerial = documentSerialNumber;

        Padding = new Padding(8);
        TranscriptScroll.Content = TranscriptStack;
        TranscriptScroll.SizeChanged += (_, _) => ApplyBubbleWidths();

        AgentPicker.SelectedValueChanged += OnAgentPicked;
        RecentPicker.SelectedValueChanged += OnRecentPicked;
        SendButton.Click += (_, _) => OnSendOrStop();

        Button settingsGear = new() { Text = "⚙", ToolTip = "AI Settings" };
        settingsGear.Click += (_, _) => OpenSettings();

        Button newConvo = new() { Text = "New", ToolTip = "New conversation (Ctrl+Shift+N)" };
        newConvo.Click += (_, _) => OnNewConversation();

        Rhino.UI.Controls.ImageButton attachButton = new()
        {
            ToolTip = "Attach a file",
            Width = 32,
            Image = PaperclipIcon()
        };

        attachButton.Click += (_, _) => OnPickFile();

        PromptBox.KeyDown += OnPromptKeyDown;
        PromptBox.TextChanged += (_, _) => GrowPromptToFit();
        // Re-measure on width change too: a panel resize re-wraps already-entered text, so without
        // this the box keeps a stale height and clips (narrower) or over-reserves (wider) until the
        // next keystroke. SizeChanged also fires once after first layout, seeding the initial height.
        PromptBox.SizeChanged += (_, _) => GrowPromptToFit();

        // Panel-wide shortcuts (Esc / focus-prompt / new-conversation) work wherever focus sits in the
        // panel; child KeyDown bubbles up to the container, so one handler here covers the whole panel.
        KeyDown += OnPanelKeyDown;

        // Focus tracking for the unread cue: any descendant gaining focus, or the pointer entering the
        // panel, counts as the user's attention and clears the cue; losing focus marks it unfocused.
        GotFocus += (_, _) => MarkFocused();
        MouseEnter += (_, _) => MarkFocused();
        PromptBox.GotFocus += (_, _) => MarkFocused();
        LostFocus += (_, _) => PanelFocused = false;
        Shown += (_, _) => MarkFocused();   // becoming visible means the user is on it now

        // Two rows so the agent picker gets the full panel width instead of fighting the model
        // label / recents / New for space on a narrow docked panel.
        StackLayout headerTop = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(settingsGear, false),
                new StackLayoutItem(AgentPicker, true),
                new StackLayoutItem(newConvo, false),
            },
        };

        StackLayout headerSub = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(ModelLabel, false),
                new StackLayoutItem(RecentPicker, true),
            },
        };

        // Eto TextArea has no internal padding on every platform, so the borderless box is wrapped in
        // a bordered Panel whose Padding insets the text off the edge. The panel carries the visible
        // frame; the inner TextArea is borderless so the two don't double up.
        PromptBox.Border = BorderType.None;
        Panel promptFrame = new()
        {
            Padding = new Padding(PromptInset),
            BackgroundColor = SystemColors.ControlBackground,
            Content = PromptBox,
        };

        StackLayout promptRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(attachButton, false),
                new StackLayoutItem(promptFrame, true),
                new StackLayoutItem(SendButton, false),
            },
        };

        // The transcript with the thinking strip pinned flush to its bottom edge: zero inner spacing
        // keeps the strip tight to the conversation, and it collapses to nothing when idle.
        TableLayout chatBox = new()
        {
            Spacing = new Size(0, 0),
            Rows =
            {
                new TableRow(TranscriptScroll) { ScaleHeight = true },
                new TableRow(ThinkingBar),
            },
        };

        Content = new TableLayout
        {
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(headerTop),
                new TableRow(headerSub),
                new TableRow(UnreadBanner),
                new TableRow(chatBox) { ScaleHeight = true },
                new TableRow(AttachmentStrip),
                new TableRow(promptRow),
            },
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Reload();
        // Measure the initial single-line height rather than trusting the literal Height initializer
        // to stay correct across DPI; SizeChanged covers later resizes.
        GrowPromptToFit();
    }

    // The Conversation is owned by the pooled CliAgent in AgentHost and outlives this panel, so
    // its off-thread Changed event would otherwise keep firing into a disposed panel (AppKit abort
    // / leak). Detach on unload; Loaded then gates any Rerender already queued via InvokeOnUiThread.
    protected override void OnUnLoad(EventArgs e)
    {
        if (Subscribed is not null && SubscribedHandler is not null)
            Subscribed.Changed -= SubscribedHandler;
        Subscribed = null;
        SubscribedHandler = null;
        base.OnUnLoad(e);
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

    private void Reload()
    {
        PopulateAgents();
        PopulateRecents();
        Resubscribe();
        Rerender();
    }

    private void PopulateAgents()
    {
        Populating = true;
        try
        {
            AgentPicker.Items.Clear();
            IReadOnlyList<ResolvedAgent> chain = AgentRegistry.Chain;
            foreach (ResolvedAgent r in chain)
            {
                string suffix = !r.Definition.Enabled ? " (disabled)" : !r.Available ? " (not found)" : string.Empty;
                AgentPicker.Items.Add(new ListItem { Text = r.Definition.Name + suffix, Key = r.Definition.Name });
            }

            // Reflect the resolved active agent without pinning: only a genuine user pick should
            // pin via AgentHost.SetActive, so the registry's first-Enabled-and-Available fallback
            // stays authoritative as availability changes.
            if (TryDoc(out RhinoDoc doc) && AgentHost.TryFor(doc, out IAgentRunner active))
                AgentPicker.SelectedKey = active.Name;
            else if (chain.FirstOrDefault(static r => r.Definition.Enabled && r.Available) is { } available)
                AgentPicker.SelectedKey = available.Definition.Name;
            else if (AgentPicker.Items.Count > 0)
                AgentPicker.SelectedIndex = 0;
        }
        finally
        {
            Populating = false;
        }

        LastAgentKey = AgentPicker.SelectedKey ?? string.Empty;
        UpdateModelLabel();
    }

    private void UpdateModelLabel()
    {
        string key = AgentPicker.SelectedKey ?? string.Empty;
        ResolvedAgent? match = AgentRegistry.Chain.FirstOrDefault(r => r.Definition.Name == key);
        string model = match is { Definition.Model: { Length: > 0 } m } ? m : "default";
        ModelLabel.Text = $"model: {model}";
    }

    private void PopulateRecents()
    {
        Populating = true;
        try
        {
            RecentPicker.Items.Clear();
            RecentPicker.Items.Add(new ListItem { Text = "Prev Convos", Key = string.Empty });
            foreach (ConversationDto convo in ConversationStore.List())
                RecentPicker.Items.Add(new ListItem { Text = RecentLabel(convo), Key = convo.SessionId });
            RecentPicker.SelectedIndex = 0;
        }
        finally
        {
            Populating = false;
        }
    }

    private static string RecentLabel(ConversationDto convo)
    {
        string when = convo.StartedAt.ToLocalTime().ToString("MMM d HH:mm");
        string first = convo.Turns.Count > 0 ? convo.Turns[0].Prompt : "(empty)";
        if (first.Length > 32)
            first = first[..32] + "…";
        return $"{when} · {convo.AgentName} · {first}";
    }

    private void OnAgentPicked(object? sender, EventArgs e)
    {
        if (Populating)
            return;

        string? name = AgentPicker.SelectedKey;
        if (string.IsNullOrEmpty(name) || !TryDoc(out RhinoDoc doc))
            return;

        // Disabled / not-installed agents are listed for context but can't be driven, and Eto has no
        // per-item disable, so snap the selection back to the last usable pick instead.
        if (AgentRegistry.Chain.FirstOrDefault(r => r.Definition.Name == name) is not { Definition.Enabled: true, Available: true })
        {
            Populating = true;
            try { AgentPicker.SelectedKey = LastAgentKey; }
            finally { Populating = false; }
            return;
        }

        LastAgentKey = name;
        Reviewing = null;   // switching agents returns to a live view
        ResetUnread();      // the new agent's conversation diffs from a clean snapshot
        AgentHost.SetActive(doc, name);
        UpdateModelLabel();
        Resubscribe();
        Rerender();
    }

    private void OnRecentPicked(object? sender, EventArgs e)
    {
        if (Populating)
            return;

        string? sessionId = RecentPicker.SelectedKey;
        if (string.IsNullOrEmpty(sessionId) || !ConversationStore.TryLoad(sessionId, out ConversationDto convo))
        {
            ExitReview();
            return;
        }
        Reviewing = convo;
        RenderReview(convo);
    }

    // Resume a reviewed past conversation: make it the doc's live conversation and seed the agent's
    // CLI session to --resume the saved id, so the next prompt continues with prior context. The
    // current live conversation (if any) is persisted first so switching away never loses it. Then
    // exit review onto the restored live transcript; the next prompt launches the CLI with --resume.
    private void ResumeReviewed(ConversationDto dto)
    {
        if (!TryDoc(out RhinoDoc doc))
            return;

        // Resume tears down (disposes) the doc's pooled runner for this agent kind. Disposing it
        // mid-turn would kill the streaming process and silently abandon the in-flight answer (the
        // ObjectDisposedException is swallowed downstream). Guard like every other live-mutation path
        // (OnSendOrStop): refuse while a turn is running rather than abort it.
        if (TurnRunning())
        {
            RhinoApp.WriteLine("[rhmcp] cannot resume while a turn is running; stop it first.");
            return;
        }

        if (TryActiveConversation(out Conversation current))
            PersistConversationHook(current);

        if (!AgentHost.TryResume(doc, dto, out IAgentRunner _))
        {
            RhinoApp.WriteLine($"[rhmcp] cannot resume: agent '{dto.AgentName}' is no longer available.");
            return;
        }

        // Reflect the resumed agent in the picker without re-triggering OnAgentPicked (which would
        // clear Reviewing and re-pin); we pin LastAgentKey here so a later New/agent-switch is correct.
        Populating = true;
        try { AgentPicker.SelectedKey = dto.AgentName; }
        finally { Populating = false; }
        LastAgentKey = dto.AgentName;
        UpdateModelLabel();

        Reviewing = null;
        ResetUnread();
        Populating = true;
        try { RecentPicker.SelectedIndex = 0; }
        finally { Populating = false; }

        Resubscribe();   // hook the restored conversation's Changed before its first prompt streams
        Rerender();
    }

    // Leave the read-only transcript and return to the live conversation.
    private void ExitReview()
    {
        Reviewing = null;
        Populating = true;
        try { RecentPicker.SelectedIndex = 0; }
        finally { Populating = false; }
        Rerender();
    }

    // Subscribe to the active agent's Conversation so writes from the off-thread reader loop
    // re-render the transcript; unhook the previous one first. No agent yet -> nothing to hook.
    private void Resubscribe()
    {
        if (Subscribed is not null && SubscribedHandler is not null)
            Subscribed.Changed -= SubscribedHandler;
        Subscribed = null;
        SubscribedHandler = null;

        if (!TryActiveConversation(out Conversation convo))
            return;

        Action handler = OnConversationChanged;
        convo.Changed += handler;
        Subscribed = convo;
        SubscribedHandler = handler;
    }

    private bool TryActiveConversation(out Conversation convo)
    {
        if (TryDoc(out RhinoDoc doc) && AgentHost.TryFor(doc, out IAgentRunner agent))
        {
            convo = agent.Conversation;
            return true;
        }
        convo = default!;
        return false;
    }

    // Off-thread (reader loop). Eto is UI-thread-only, so marshal the rebuild.
    private void OnConversationChanged() => RhinoApp.InvokeOnUiThread(new Action(Rerender));

    private void Rerender()
    {
        // A Rerender queued onto the UI thread can land after the panel is unloaded; mutating
        // detached Eto controls risks an AppKit abort / ObjectDisposedException.
        if (!Loaded)
            return;

        // While reviewing a persisted transcript, a live Changed event must not clobber the
        // read-only view; the review is redrawn only by OnRecentPicked / Back.
        if (Reviewing is { } reviewed)
        {
            RenderReview(reviewed);
            return;
        }

        bool anyAgent = AgentRegistry.Chain.Any(static r => r.Available);
        if (!anyAgent)
        {
            ResetTranscript();
            ShowNoAgentState();
            SyncSendButton(false);
            ResetUnread();
            return;
        }

        if (!TryActiveConversation(out Conversation convo))
        {
            ResetTranscript();
            RenderItem(new TranscriptItem(TranscriptRole.Agent, "Start a conversation with the active agent."));
            ApplyBubbleWidths();
            SyncSendButton(false);
            ResetUnread();
            return;
        }

        // Happy path: an agent is available and resolved, so warm the doc's MCP listener now rather
        // than lazily on the first prompt. Idempotent (a started listener is reused), so re-running
        // on every Rerender is harmless. Failure is silent here; the prompt path reports if it bites.
        if (TryDoc(out RhinoDoc liveDoc))
            AgentDispatch.TryEnsureListener(liveDoc, out int _);

        TranscriptViewModel vm = TranscriptViewModel.FromLive(convo);
        ComputeUnread(vm);

        // An agent is ready but the conversation has no turns yet: offer one-tap GH2 starters
        // instead of an empty pane. They vanish the moment a turn starts (vm.Items fills).
        if (vm.Items.Count == 0 && !convo.TryGetPendingQuestion(out _))
        {
            ResetTranscript();
            ShowStarterChips();
            SyncSendButton(false);
            ApplyBubbleWidths();
            return;
        }

        // Incremental: diff vm.Items against the materialized rows and touch only what changed (a
        // streaming assistant delta grows the last bubble; a new chip appends; a tool result folds
        // into its chip). The prior special-state branches each reset, so a stale non-item layout
        // can't survive into here, but guard anyway and rebuild from scratch if it does.
        if (!Reconcilable)
            ResetTranscript();
        Reconcilable = true;

        // The trailing ask_user card is rebuilt fresh each render (it captures the live question), so
        // drop it before reconciling, otherwise a newly appended item row would land after it.
        ClearQuestionCard();
        ReconcileItems(vm.Items);
        SyncQuestionCard(convo);

        SyncSendButton(vm.Running);
        ApplyBubbleWidths();
        ScrollToBottom();
    }

    // Reconcile the materialized rows against the new item list: keep the matching prefix, update the
    // first divergent row in place when the change is a streaming delta / tool-result fold, otherwise
    // tear down from that row and rebuild the suffix, then append any trailing new items. Earlier,
    // unchanged turns are never rebuilt.
    private void ReconcileItems(IReadOnlyList<TranscriptItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (i < Rendered.Count)
            {
                if (Rendered[i].Item == items[i])
                    continue;
                if (TryUpdateInPlace(i, items[i]))
                    continue;
                TruncateRowsFrom(i);
            }
            AppendRow(items[i]);
        }
        if (Rendered.Count > items.Count)
            TruncateRowsFrom(items.Count);
    }

    // The two streaming shapes that can update a row without a rebuild: an assistant/user bubble whose
    // text grew (same role) and a tool chip whose result just folded in (same header + args). Anything
    // else (role change, system text change, args change) is a structural change the caller rebuilds.
    private bool TryUpdateInPlace(int index, TranscriptItem item)
    {
        RenderedRow row = Rendered[index];
        if (row.Item.Role != item.Role)
            return false;

        switch (item.Role)
        {
            case TranscriptRole.User:
            case TranscriptRole.Agent:
                if (row.Bubble is not { } bubble)
                    return false;
                bubble.Update(item.Text);
                // The turn's tokens fold onto its last bubble at completion, so refresh the meta too,
                // not just the bubble text.
                row.Meta?.Update(item.Text, item.Timestamp, item.Usage);
                Rendered[index] = row with { Item = item };
                return true;

            case TranscriptRole.Tool when row.Item.Text == item.Text && row.Item.ToolArgs == item.ToolArgs:
                Control chip = ToolChip(item, out Label? detail);
                // RemoveAt+Insert, not an indexer set: StackLayout reliably relays out on the
                // Add/Remove CollectionChanged that the append/truncate paths already depend on; a
                // Replace notification is not a proven trigger for an Eto relayout.
                TranscriptStack.Items.RemoveAt(index);
                TranscriptStack.Items.Insert(index, new StackLayoutItem(chip));
                RenderedRow updated = new(item, chip, null, null, detail);
                Rendered[index] = updated;
                if (LastBudget > 0)
                    PinRow(updated, LastBudget);   // the folded-in result spawns a fresh detail label
                return true;

            default:
                return false;
        }
    }

    private void AppendRow(TranscriptItem item)
    {
        RenderedRow row = BuildRow(item);
        Rendered.Add(row);
        TranscriptStack.Items.Add(row.Control);
        if (LastBudget > 0)
            PinRow(row, LastBudget);   // a freshly appended row must size even at a stable budget
    }

    // Drop every materialized row from `index` on, so a structural change rebuilds only the divergent
    // suffix, never the unchanged prefix. The caller clears the question card before reconciling, so
    // the only trailing controls here are item rows.
    private void TruncateRowsFrom(int index)
    {
        for (int i = Rendered.Count - 1; i >= index; i--)
        {
            TranscriptStack.Items.RemoveAt(i);
            Rendered.RemoveAt(i);
        }
    }

    // Append the trailing ask_user card, if a question is pending, just after the item rows. The
    // caller clears any prior card first, so this only ever adds.
    private void SyncQuestionCard(Conversation convo)
    {
        if (convo.TryGetPendingQuestion(out PendingQuestion question))
        {
            QuestionRow = QuestionCard(question);
            TranscriptStack.Items.Add(QuestionRow);
        }
    }

    private void ClearQuestionCard()
    {
        if (QuestionRow is null)
            return;
        TranscriptStack.Items.RemoveAt(Rendered.Count);
        QuestionRow = null;
    }

    // Clear every materialized row, the question card, and per-render tracking, and force the next
    // ApplyBubbleWidths to re-pin widths over freshly built controls even when the viewport is stable.
    private void ResetTranscript()
    {
        TranscriptStack.Items.Clear();
        Rendered.Clear();
        QuestionRow = null;
        Reconcilable = false;
        LastBudget = -1;
    }

    // Used by the non-incremental paths (review, single-message). Builds the row and appends it.
    private void RenderItem(TranscriptItem item)
    {
        RenderedRow row = BuildRow(item);
        Rendered.Add(row);
        TranscriptStack.Items.Add(row.Control);
    }

    private RenderedRow BuildRow(TranscriptItem item)
    {
        switch (item.Role)
        {
            case TranscriptRole.System:
                return new RenderedRow(item, SystemLine(item.Text), null, null, null);
            case TranscriptRole.Tool:
                Control chip = ToolChip(item, out Label? detail);
                return new RenderedRow(item, chip, null, null, detail);
            default:
                Control row = BubbleRow(item, out MessageBubble bubble, out MessageMeta meta);
                return new RenderedRow(item, row, bubble, meta, null);
        }
    }

    // A bubble biased left (agent) or right (user) by a stretchable spacer on the open side; the
    // transcript's side padding plus the spacer give every row left/right breathing room. Below the
    // bubble sits a hover-revealed meta line (copy / time / tokens), hidden until the pointer is over
    // the bubble+meta column. The bubble and meta are handed back so the row can width-pin the bubble
    // and update both in place on a streaming delta.
    private Control BubbleRow(TranscriptItem item, out MessageBubble bubble, out MessageMeta meta)
    {
        bool user = item.Role == TranscriptRole.User;
        bubble = new(item.Text, user, MeasureFont);

        MessageMeta line = new(user, CopyIcon());
        line.Update(item.Text, item.Timestamp, item.Usage);
        meta = line;

        // Bubble stacked over its meta, both aligned to the message's biased edge. The meta reserves a
        // fixed height, so revealing it on hover never moves rows below. Container-level MouseEnter/
        // MouseLeave is reliable across child controls on both backends (the children sit inside the
        // column's bounds), so one pair of handlers reveals the line; RefreshTime re-stamps the
        // relative time at the moment of hover.
        StackLayout column = new()
        {
            Spacing = 2,
            HorizontalContentAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Items = { new StackLayoutItem(bubble, false), new StackLayoutItem(line, false) },
        };
        column.MouseEnter += (_, _) => { line.RefreshTime(); line.Show(true); };
        column.MouseLeave += (_, _) => line.Show(false);

        StackLayout row = new() { Orientation = Orientation.Horizontal };
        StackLayoutItem flex = new(null, true);
        StackLayoutItem fixedSide = new(column, false);
        if (user)
        {
            row.Items.Add(flex);
            row.Items.Add(fixedSide);
        }
        else
        {
            row.Items.Add(fixedSide);
            row.Items.Add(flex);
        }
        return row;
    }

    private bool TurnRunning()
    {
        if (!TryActiveConversation(out Conversation convo))
            return false;
        IReadOnlyList<Turn> turns = convo.Turns;
        return turns.Count > 0 && !turns[^1].Completed;
    }

    // Inline ask_user affordance rendered at the bottom of the transcript. The tool already returned
    // (non-blocking): submitting the card dispatches the chosen option label(s) as the agent's NEXT
    // prompt, which the same live pooled agent reads as the answer and continues from. Click handlers
    // run on the UI thread and only touch managed state and AgentDispatch (no Rhino modal Get APIs),
    // so the card is safe to answer mid-command.
    private Control QuestionCard(PendingQuestion question)
    {
        StackLayout body = new() { Spacing = 6, Padding = new Padding(8, 6) };
        body.Items.Add(new Label { Text = question.Question, Font = SystemFonts.Bold() });

        TextBox otherText = new() { PlaceholderText = "Other…" };

        if (question.Mode == AskUserMode.Multi)
        {
            List<CheckBox> checks = [];
            foreach (string option in question.Options)
            {
                CheckBox box = new() { Text = option };
                checks.Add(box);
                body.Items.Add(box);
            }
            body.Items.Add(otherText);

            Button submit = new() { Text = "Submit" };
            submit.Click += (_, _) =>
            {
                List<string> selected = [];
                for (int i = 0; i < checks.Count; i++)
                    if (checks[i].Checked == true)
                        selected.Add(question.Options[i]);
                string other = otherText.Text?.Trim() ?? string.Empty;
                if (other.Length > 0)
                    selected.Add(other);
                CompleteFromPanel(question, selected);
            };
            body.Items.Add(ButtonRow(question, submit));
            return Card(body);
        }

        RadioButton controller = new() { Text = question.Options.Count > 0 ? question.Options[0] : "Other" };
        List<RadioButton> radios = [];
        for (int i = 0; i < question.Options.Count; i++)
        {
            RadioButton radio = i == 0 ? controller : new RadioButton(controller) { Text = question.Options[i] };
            radios.Add(radio);
            body.Items.Add(radio);
        }
        RadioButton otherRadio = question.Options.Count == 0 ? controller : new RadioButton(controller) { Text = "Other" };
        if (question.Options.Count != 0)
            body.Items.Add(otherRadio);
        body.Items.Add(otherText);

        Button submitSingle = new() { Text = "Submit" };
        submitSingle.Click += (_, _) =>
        {
            for (int i = 0; i < radios.Count; i++)
            {
                if (radios[i].Checked)
                {
                    CompleteFromPanel(question, [question.Options[i]]);
                    return;
                }
            }
            string other = otherText.Text?.Trim() ?? string.Empty;
            CompleteFromPanel(question, other.Length > 0 ? [other] : []);
        };
        body.Items.Add(ButtonRow(question, submitSingle));
        return Card(body);
    }

    private Control ButtonRow(PendingQuestion question, Button submit)
    {
        Button cancel = new() { Text = "Cancel" };
        cancel.Click += (_, _) => DismissQuestion(question);
        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Items = { submit, cancel },
        };
    }

    // Submit the panel choice: dispatch the selected label(s) as the agent's next prompt (the live
    // pooled agent resumes and reads it as the answer), then clear the card. An empty selection is
    // treated like Cancel: clear the card without prompting.
    private void CompleteFromPanel(PendingQuestion question, IReadOnlyList<string> selected)
    {
        if (selected.Count == 0)
        {
            DismissQuestion(question);
            return;
        }
        if (!TryDoc(out RhinoDoc doc))
            return;

        // First-wins through the SAME claim the command-line picker uses: if the picker already
        // dispatched this question, we lose the claim and do nothing (re-render to drop the card).
        if (!AskUserPicker.TryClaim(doc.RuntimeSerialNumber, question))
        {
            Rerender();
            return;
        }

        // Park the answer and clear the card unconditionally: AnswerActive guarantees delivery (it
        // dispatches now if the gate is free, otherwise holds the answer and flushes it the instant
        // the running turn ends), so the answer is never lost and there is nothing to retry.
        AgentDispatch.AnswerActive(doc, UserMessage.FromText(string.Join(", ", selected)));
        if (TryActiveConversation(out Conversation convo))
            convo.ClearPendingQuestion(question);
        Rerender();
    }

    // Drop the pending question without answering it (Cancel, or an empty submit).
    private void DismissQuestion(PendingQuestion question)
    {
        if (TryDoc(out RhinoDoc doc))
            // First-wins: a panel dismiss aborts the command-line GetOption picker still running for
            // this question, so the picker can't dispatch a now-cancelled question.
            AskUserPicker.Cancel(doc.RuntimeSerialNumber, question);
        if (TryActiveConversation(out Conversation convo))
            convo.ClearPendingQuestion(question);
        Rerender();
    }

    private static Control Card(Control content) => new Panel
    {
        Padding = new Padding(2),
        BackgroundColor = SystemColors.ControlBackground,
        Content = content,
    };

    // Read-only render of a persisted transcript. Mirrors the live layout (bubbles + tool chips)
    // but drives off the DTO and is never mutated by the off-thread Changed event.
    private void RenderReview(ConversationDto convo)
    {
        if (!Loaded)
            return;

        ResetTranscript();
        ResetUnread();   // the read-only view has no live stream to flag

        Button back = new() { Text = "← Back to live" };
        back.Click += (_, _) => ExitReview();

        // Resume only when the saved conversation's agent is enabled AND available, matching the agent
        // picker's drivability check (OnAgentPicked): a merely-registered-but-disabled / not-found
        // agent has no launchable runner, so offer review only rather than a Resume that would fault.
        Button resume = new() { Text = "↩ Resume", ToolTip = "Continue this conversation with the agent" };
        resume.Enabled = AgentRegistry.Chain.Any(r => r.Definition.Name == convo.AgentName && r is { Definition.Enabled: true, Available: true });
        resume.Click += (_, _) => ResumeReviewed(convo);

        TranscriptStack.Items.Add(new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Items = { back, resume },
        });
        TranscriptStack.Items.Add(SystemLine($"{convo.DocTitle} · {convo.AgentName} (read-only)"));

        TranscriptViewModel vm = TranscriptViewModel.FromReview(convo);
        foreach (TranscriptItem item in vm.Items)
            RenderItem(item);

        SyncSendButton(false);
        SendButton.Enabled = false;
        ApplyBubbleWidths();
        ScrollToBottom();
    }

    private static Control SystemLine(string text) => new Label
    {
        Text = text,
        TextAlignment = TextAlignment.Center,
        TextColor = SystemColors.DisabledText,
        Font = SystemFonts.Default(7),
    };

    // Compact one-line chip; click toggles an expander showing the tool's args + result. The detail
    // label is handed back (null when there's nothing to expand) so the row can wrap it to the
    // viewport (long JSON otherwise widens the transcript and forces a horizontal scroll).
    private Control ToolChip(TranscriptItem item, out Label? detail)
    {
        string body = BuildToolDetail(item.ToolArgs, item.ToolResult);
        string header = item.Summary.Length > 0 ? item.Summary : item.Text;
        Label headerLabel = new()
        {
            Text = $"⚙ {header}",
            Font = SystemFonts.Default(8),
            TextColor = SystemColors.ControlText,
        };

        if (body.Length == 0)
        {
            detail = null;
            return new Panel { Padding = new Padding(6, 3), Content = headerLabel };
        }

        detail = new Label
        {
            Text = body,
            Wrap = WrapMode.Word,
            Font = SystemFonts.Default(8),
            TextColor = SystemColors.DisabledText,
        };

        Expander expander = new()
        {
            Header = headerLabel,
            Expanded = false,
            Content = detail,
        };
        return new Panel { Padding = new Padding(6, 3), Content = expander };
    }

    private static string BuildToolDetail(string args, string result)
    {
        System.Text.StringBuilder sb = new();
        if (!string.IsNullOrWhiteSpace(args))
            sb.Append("args: ").Append(args);
        if (!string.IsNullOrWhiteSpace(result))
        {
            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.Append("result: ").Append(result);
        }
        return sb.ToString();
    }

    private static Image? CopyIcon() => LoadIcon("RhMcp.copy.svg", 14);

    private static Image? PaperclipIcon() => LoadIcon("RhMcp.paperclip.svg", 18);

    // An embedded single-color SVG recolored to the current theme's foreground (so the glyph reads in
    // both light and dark mode) and rasterized to an Eto bitmap. Cached by resource/size/color, so a
    // theme switch re-renders rather than serving a stale bitmap. Null on any failure (text fallback).
    private static Image? LoadIcon(string resourceName, int size)
    {
        string hex = HexColor(SystemColors.ControlText);
        string cacheKey = $"{resourceName}@{size}@{hex}";
        if (IconCache.TryGetValue(cacheKey, out Image? cached))
            return cached;

        Image? icon = null;
        try
        {
            Assembly assembly = typeof(AIPAnel).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                using StreamReader reader = new(stream);
                string svg = reader.ReadToEnd().Replace("#1A1A1A", hex);
                using System.Drawing.Bitmap rendered = Rhino.UI.DrawingUtilities.BitmapFromSvg(svg, size, size, adjustForDarkMode: false);
                using MemoryStream png = new();
                rendered.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                icon = new Bitmap(png.ToArray());
            }
        }
        catch
        {
            icon = null;
        }

        IconCache[cacheKey] = icon;
        return icon;
    }

    private static string HexColor(Color c) =>
        $"#{(int)(c.R * 255 + 0.5f):X2}{(int)(c.G * 255 + 0.5f):X2}{(int)(c.B * 255 + 0.5f):X2}";

    // Empty-conversation discoverability: a stacked column of clickable starters. Clicking one drops
    // the text into the prompt box and routes through the normal Send path, so the chips disappear on
    // the next Rerender as the turn begins.
    private void ShowStarterChips()
    {
        StackLayout chips = new()
        {
            Spacing = 6,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items =
            {
                new Label
                {
                    Text = "Try one of these to get going:",
                    TextColor = SystemColors.DisabledText,
                    TextAlignment = TextAlignment.Center,
                },
            },
        };

        foreach (string prompt in StarterPrompts)
        {
            LinkButton chip = new() { Text = prompt };
            chip.Click += (_, _) => SendStarter(prompt);
            chips.Items.Add(chip);
        }

        TranscriptStack.Items.Add(chips);
    }

    private void SendStarter(string prompt)
    {
        PromptBox.Text = prompt;
        Send();
    }

    private void ShowNoAgentState()
    {
        LinkButton docs = new() { Text = "Install and sign in to an agent →" };
        docs.Click += (_, _) => Application.Instance.Open(DocsLinks.GettingStarted);

        Button open = new() { Text = "Open AI Settings" };
        open.Click += (_, _) => OpenSettings();

        TranscriptStack.Items.Add(new StackLayout
        {
            Spacing = 8,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items =
            {
                new Label { Text = "No AI agent found.", Font = SystemFonts.Bold() },
                new Label
                {
                    Text = "Install an agent (Claude, Codex, or Gemini) and sign in to get started.",
                    Wrap = WrapMode.Word,
                    TextAlignment = TextAlignment.Center,
                },
                docs,
                open,
            },
        });
    }

    private void SyncSendButton(bool running)
    {
        SendButton.Enabled = true;   // re-enable after a read-only review disabled it
        SendButton.Text = running ? "Stop" : "Send";
        SendButton.ToolTip = running ? "Cancel the current turn (Esc)" : "Send (Enter)";
        // The agent isn't "thinking" while it's blocked on a pending ask_user answer, so drop the cue.
        ThinkingBar.Visible = running && !QuestionPending();
    }

    private bool QuestionPending() =>
        TryActiveConversation(out Conversation convo) && convo.TryGetPendingQuestion(out _);

    // Raise the unread cue from a live render when the panel isn't focused: a turn finishing is the
    // strongest signal (and supersedes a streaming cue), output arriving mid-turn the lighter one.
    // While focused nothing accumulates. The snapshot always advances so the next render diffs cleanly.
    private void ComputeUnread(TranscriptViewModel vm)
    {
        if (!PanelFocused)
        {
            if (WasRunning && !vm.Running)
                Unread = UnreadCue.TurnComplete;
            else if (vm.Running && vm.Items.Count > LastItemCount && Unread == UnreadCue.None)
                Unread = UnreadCue.Streaming;
        }

        WasRunning = vm.Running;
        LastItemCount = vm.Items.Count;
        UpdateUnreadBanner();
    }

    // The banner is UI = f(Unread): a dot for streaming output, a check for turn-complete, hidden when
    // there's nothing to flag. Cheap enough to recompute every render.
    private void UpdateUnreadBanner()
    {
        switch (Unread)
        {
            case UnreadCue.Streaming:
                UnreadBanner.Text = "● New agent output";
                UnreadBanner.TextColor = SystemColors.LinkText;
                UnreadBanner.Visible = true;
                break;
            case UnreadCue.TurnComplete:
                UnreadBanner.Text = "✓ Response ready";
                UnreadBanner.TextColor = SystemColors.LinkText;
                UnreadBanner.Visible = true;
                break;
            default:
                UnreadBanner.Text = string.Empty;
                UnreadBanner.Visible = false;
                break;
        }
    }

    // Clear the cue and its transition snapshot. Called when the live context changes underneath the
    // panel (new conversation, agent switch, review) so a stale snapshot can't fire a phantom cue and
    // a left-over banner can't survive into a non-live state.
    private void ResetUnread()
    {
        Unread = UnreadCue.None;
        WasRunning = false;
        LastItemCount = 0;
        UpdateUnreadBanner();
    }

    // Eto layout is deferred, so TranscriptStack's size is stale right after a rebuild; defer the
    // scroll until after layout settles so streaming actually reaches the true bottom.
    private void ScrollToBottom() => Application.Instance.AsyncInvoke(() =>
    {
        if (Loaded)
            TranscriptScroll.ScrollPosition = new Point(0, TranscriptStack.Height);
    });

    // The width budget every row wraps to: the viewport minus the transcript's side padding and a
    // reserve for the vertical scrollbar, so nothing reports a width wider than the viewport (which
    // would otherwise show a horizontal scrollbar). Re-runs on resize; a no-op when width is stable
    // (an incremental render pins its own new/updated rows via PinRow, so the unchanged rest is fine).
    private void ApplyBubbleWidths()
    {
        int viewport = TranscriptScroll.ClientSize.Width;
        if (viewport <= 0)
        {
            // First render before the Scrollable has laid out: retry once layout settles so a bubble
            // streaming on the very first turn gets a real width budget instead of staying unpinned.
            Application.Instance.AsyncInvoke(() =>
            {
                if (Loaded && TranscriptScroll.ClientSize.Width > 0)
                    ApplyBubbleWidths();
            });
            return;
        }
        int budget = Math.Max(80, viewport - 2 * SideMargin - ScrollbarGuard);
        if (budget == LastBudget)
            return;
        LastBudget = budget;

        foreach (RenderedRow row in Rendered)
            PinRow(row, budget);
    }

    private static void PinRow(RenderedRow row, int budget)
    {
        row.Bubble?.Apply(budget);
        if (row.Detail is { } detail)
            detail.Width = budget;
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Keys.Enter && !e.Modifiers.HasFlag(Keys.Shift))
        {
            e.Handled = true;
            OnSendOrStop();
            return;
        }

        // Intercept paste only when the clipboard carries an image or file uris; let plain-text
        // paste fall through to the TextArea's own handling.
        bool pasteChord = e.Key == Keys.V && (e.Modifiers.HasFlag(Keys.Application) || e.Modifiers.HasFlag(Keys.Control));
        if (pasteChord && TryPasteAttachment())
            e.Handled = true;
    }

    // Auto-grow the prompt box with its content: measure the wrapped line count at the current text
    // width and set the box height between PromptMin/MaxHeight. Past the cap it scrolls internally.
    // The promptRow TableRow is not ScaleHeight, so growing PromptBox grows the bottom region and the
    // ScaleHeight transcript row above gives up the space. Skipped until laid out (Width <= 0).
    private void GrowPromptToFit()
    {
        if (!Loaded)
            return;

        // PromptBox is borderless and is the Content of promptFrame (which carries the PromptInset
        // padding), so PromptBox.Width is already the inner text width and PromptBox.Height is the
        // text area height: the frame adds the visual inset outside the box. Measure rows-only here;
        // do not re-add the inset on either axis or it double-counts and over-wraps / over-grows.
        int width = PromptBox.Width;
        if (width <= 0)
            return;

        int target = TextMeasure.WrappedHeight(MeasureFont, PromptBox.Text ?? string.Empty, width);
        int clamped = Math.Clamp(target, PromptMinHeight, PromptMaxHeight);
        if (PromptBox.Height != clamped)
            PromptBox.Height = clamped;
    }

    // Panel-wide shortcuts, reached via child KeyDown bubbling so they fire wherever focus sits:
    //   Esc            stop the running turn (no-op when idle, so it falls through harmlessly)
    //   Ctrl/Cmd+L     focus the prompt box
    //   Ctrl/Cmd+Shift+N  start a new conversation (Shift avoids clobbering Rhino's own Ctrl+N "New")
    // Enter / Shift+Enter / paste stay on PromptBox.KeyDown and are untouched.
    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        bool mod = e.Modifiers.HasFlag(Keys.Application) || e.Modifiers.HasFlag(Keys.Control);

        if (e.Key == Keys.Escape && TurnRunning())
        {
            CancelActive();
            e.Handled = true;
            return;
        }

        if (mod && e.Key == Keys.L)
        {
            PromptBox.Focus();
            e.Handled = true;
            return;
        }

        if (mod && e.Modifiers.HasFlag(Keys.Shift) && e.Key == Keys.N)
        {
            OnNewConversation();
            e.Handled = true;
        }
    }

    // The user is attending to the panel: clear the unread cue and remember focus so subsequent agent
    // output doesn't re-raise it. Only ever called from Eto focus/pointer handlers, so already on the
    // UI thread; the banner mutation is safe directly.
    private void MarkFocused()
    {
        PanelFocused = true;
        if (Unread != UnreadCue.None)
        {
            Unread = UnreadCue.None;
            UpdateUnreadBanner();
        }
    }

    private void OnSendOrStop()
    {
        if (Reviewing is not null)   // read-only: Enter/click must not drive the live conversation
            return;
        if (SendButton.Text == "Stop")
        {
            CancelActive();
            return;
        }
        Send();
    }

    private void CancelActive()
    {
        if (TryDoc(out RhinoDoc doc) && AgentHost.TryFor(doc, out IAgentRunner agent))
            agent.Cancel();
    }

    private void Send()
    {
        string text = PromptBox.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 && Pending.Count == 0)
            return;
        if (!TryDoc(out RhinoDoc doc))
            return;

        // Resolve/subscribe before dispatch: the first prompt builds the agent, and we want the
        // Changed hook attached before its reader loop starts writing. Gate on availability so a
        // no-op dispatch doesn't silently discard the user's typed prompt + attachments.
        if (!AgentHost.TryFor(doc, out IAgentRunner _))
        {
            RhinoApp.WriteLine("[rhmcp] No AI agent available; open AI Settings to configure one.");
            return;
        }
        Resubscribe();

        UserMessage message = new(text, Pending.ToArray());
        AgentDispatch.PromptActive(doc, message);

        PromptBox.Text = string.Empty;
        Pending.Clear();
        RefreshAttachmentStrip();
        Rerender();
    }

    private void OnNewConversation()
    {
        Reviewing = null;   // a fresh conversation is always a live view
        ResetUnread();      // the fresh conversation starts from a clean snapshot
        if (TryActiveConversation(out Conversation convo))
            PersistConversationHook(convo);

        // Drop the pooled agent so the next prompt rebuilds it with a fresh session id +
        // Conversation. Disposal also kills any in-flight process. Stop() also forgets the pinned
        // active agent, so re-pin the dropdown's selection, otherwise New silently snaps back to
        // the configured default while the picker still shows the old choice.
        if (TryDoc(out RhinoDoc doc))
        {
            AgentHost.Stop(doc);
            if (LastAgentKey.Length > 0)
                AgentHost.SetActive(doc, LastAgentKey);
        }

        Pending.Clear();
        RefreshAttachmentStrip();
        Resubscribe();
        PopulateRecents();
        Rerender();
    }

    private void OnPickFile()
    {
        OpenFileDialog dialog = new() { MultiSelect = true, Title = "Attach files" };
        dialog.Filters.Add(new FileFilter("Images", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"));
        dialog.Filters.Add(new FileFilter("Text", ".txt", ".md", ".cs", ".py", ".json", ".csv", ".log"));
        dialog.Filters.Add(new FileFilter("All files", ".*"));

        if (dialog.ShowDialog(this) != DialogResult.Ok)
            return;

        foreach (string path in dialog.Filenames)
            AddFileAttachment(path);
        RefreshAttachmentStrip();
    }

    private void AddFileAttachment(string path)
    {
        if (!File.Exists(path))
            return;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string name = Path.GetFileName(path);
        byte[] data = File.ReadAllBytes(path);

        if (IsImageExtension(ext))
            Pending.Add(new Attachment(AttachmentKind.Image, name, MediaTypeForImage(ext), data));
        else
            Pending.Add(new Attachment(AttachmentKind.TextFile, name, "text/plain", data));
    }

    private static bool IsImageExtension(string ext) => ext switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => true,
        _ => false,
    };

    private static string MediaTypeForImage(string ext) => ext switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    private bool TryPasteAttachment()
    {
        Clipboard clipboard = Clipboard.Instance;
        if (clipboard.ContainsImage && clipboard.Image is Bitmap bitmap)
        {
            using MemoryStream ms = new();
            bitmap.Save(ms, ImageFormat.Png);
            Pending.Add(new Attachment(AttachmentKind.Image, "pasted.png", "image/png", ms.ToArray()));
            RefreshAttachmentStrip();
            return true;
        }
        if (clipboard.ContainsUris && clipboard.Uris is { Length: > 0 } uris)
        {
            foreach (Uri uri in uris.Where(static u => u.IsFile))
                AddFileAttachment(uri.LocalPath);
            RefreshAttachmentStrip();
            return true;
        }
        return false;
    }

    private void RefreshAttachmentStrip()
    {
        AttachmentStrip.Items.Clear();
        foreach (Attachment att in Pending.ToArray())
            AttachmentStrip.Items.Add(AttachmentChip(att));
    }

    private Control AttachmentChip(Attachment att)
    {
        Label name = new()
        {
            Text = att.Kind == AttachmentKind.Image ? $"▦ {att.Name}" : $"▤ {att.Name}",
            VerticalAlignment = VerticalAlignment.Center,
            Font = SystemFonts.Default(8),
        };
        Button remove = new() { Text = "×", Width = 22, ToolTip = "Remove attachment" };
        remove.Click += (_, _) =>
        {
            Pending.Remove(att);
            RefreshAttachmentStrip();
        };

        return new Panel
        {
            Padding = new Padding(6, 2),
            BackgroundColor = SystemColors.ControlBackground,
            Content = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { name, remove },
            },
        };
    }

    private void OpenSettings()
    {
        AISettingsDialog dialog = new();
        dialog.ShowModal(this);
        AgentRegistry.Refresh();
        Reload();
    }
}
