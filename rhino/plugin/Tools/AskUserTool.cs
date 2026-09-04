using System.Threading.Tasks;

namespace RhinoAI.Tools;

[McpServerToolType]
public static class AskUserTool
{
    [McpServerTool("ask_user", "Ask User", true, false)]
    [BackgroundThread]
    [InPanelOnly]
    [Description("Ask the Rhino user to choose among options when you need a decision you cannot "
        + "make yourself. Pass EVERY question you need answered in this one call: they are shown "
        + "together as one stack in the Rhino MCP panel (radio for single choice, checkboxes for "
        + "multi) and on the command line, and come back as a single reply. This tool does NOT wait "
        + "for the answers: it poses the questions and returns immediately. STOP and end your turn "
        + "after calling it; the user's answers arrive as their next message, then continue.")]
    public static async Task<object> AskUser(
        RhinoDoc doc,
        [Description("The questions to ask, in the order they should be shown")] QuestionSpec[] questions)
    {
        List<PendingQuestion> posed = [];
        foreach (QuestionSpec spec in questions ?? [])
        {
            if (spec is null || string.IsNullOrWhiteSpace(spec.Question))
                continue;

            PendingQuestion pending = new(
                spec.Question,
                spec.Options ?? [],
                spec.MultiSelect ? AskUserMode.Multi : AskUserMode.Single);

            // The constructor collapses labels the panel synthesizes, so an options list of only
            // those survives a raw length check yet leaves nothing to pick. Drop such a question
            // rather than render a dead card.
            if (pending.Options.Count > 0)
                posed.Add(pending);
        }

        if (posed.Count == 0)
            return "ask_user needs a non-empty questions array; each entry is "
                + "{ question, options, multiSelect? } and must carry at least one real option.";

        uint docSerial = doc.RuntimeSerialNumber;

        // Attach to the live Conversation so the panel renders the cards; the Conversation is the
        // single source of truth for pending questions (it survives a panel dock/undock reload,
        // since the panel rebinds to the same live instance). AgentHost's dictionaries are
        // UI-thread-owned and unsynchronized; this tool body runs off the UI thread
        // ([BackgroundThread]), so resolve the Conversation on the UI thread where the TryFor read
        // can't race a New/SetActive mutation.
        ConversationLookup lookup = await ResolveConversationAsync(doc).ConfigureAwait(false);

        // A second ask_user APPENDS rather than replacing, so an earlier unanswered question is never
        // silently evicted. Both channels then present the whole outstanding set, not just the new
        // arrivals, which is why the picker and the printed prompt below take the full list.
        IReadOnlyList<PendingQuestion> outstanding = posed;
        if (lookup.Attached)
        {
            lookup.Conversation.AddPendingQuestions(posed);
            if (lookup.Conversation.TryGetPendingQuestions(out IReadOnlyList<PendingQuestion> all))
                outstanding = all;
        }

        // Present the command-line GetOption picker on the UI thread as an answer affordance (the
        // panel card is the other channel). Walking it dispatches the answers as the next prompt and
        // clears the cards; it degrades to the printed prompt when the Get can't run (mid-command, or
        // a platform that rejects an out-of-command Get). Print the prompt FIRST so the fallback text
        // is always on the command line whether or not the picker takes over.
        PrintPrompt(outstanding);
        ShowPicker(docSerial, outstanding);

        // Non-blocking: the answers are not awaited here. The user answers in the panel (or on the
        // command line) and that reply is dispatched as the agent's NEXT prompt, resuming the same
        // live pooled agent. Return now and steer the agent to end its turn.
        return new
        {
            posed = posed.Count,
            outstanding = outstanding.Count,
            note = "Shown to the user in the Rhino panel. Stop now and end your turn; "
                + "the user's answers will be your next message, then continue.",
        };
    }

    private readonly record struct ConversationLookup(bool Attached, Conversation Conversation);

    private static Task<ConversationLookup> ResolveConversationAsync(RhinoDoc doc)
    {
        TaskCompletionSource<ConversationLookup> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try { tcs.SetResult(ResolveConversation(doc)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }), null);
        return tcs.Task;
    }

    // UI-thread only: reads AgentHost's unsynchronized dictionaries.
    private static ConversationLookup ResolveConversation(RhinoDoc doc) =>
        AgentHost.TryFor(doc, out IAgentRunner agent)
            ? new ConversationLookup(true, agent.Conversation)
            : new ConversationLookup(false, default!);

    // Fire the command-line picker on the UI thread without blocking this background tool body. The
    // picker's modal Get runs on the UI thread until the user works through the questions, the panel
    // answers (Cancel), or a superseding ask_user cancels it; the tool has already returned by then,
    // so we never await it.
    private static void ShowPicker(uint docSerial, IReadOnlyList<PendingQuestion> questions) =>
        RhinoApp.InvokeOnUiThread(new Action(() => AskUserPicker.TryShow(docSerial, questions)), null);

    private static void PrintPrompt(IReadOnlyList<PendingQuestion> questions)
    {
        for (int q = 0; q < questions.Count; q++)
        {
            PendingQuestion question = questions[q];
            string number = questions.Count > 1 ? $" ({q + 1}/{questions.Count})" : string.Empty;
            RhinoApp.WriteLine($"[ask]{number} {question.Question}");
            for (int i = 0; i < question.Options.Count; i++)
                RhinoApp.WriteLine($"  {i + 1}. {question.Options[i]}");
        }

        bool anyMulti = false;
        foreach (PendingQuestion question in questions)
            if (question.Mode == AskUserMode.Multi)
                anyMulti = true;

        string hint = anyMulti
            ? "Answer in the panel, or type \"<numbers or labels, comma-separated>\"."
            : "Answer in the panel, or type \"<number or label>\".";
        RhinoApp.WriteLine($"  {hint}");
    }
}
