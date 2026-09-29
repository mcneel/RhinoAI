using System.Threading.Tasks;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class AskUserTool
{
    [McpServerTool("ask_user", "Ask User", true, false)]
    [BackgroundThread]
    [InPanelOnly]
    [Description("Ask the Rhino user to choose among options when you need a decision you cannot "
        + "make yourself. Pass EVERY question you need answered in this one call: the Rhino AI panel "
        + "shows them one page at a time (radio for single choice, checkboxes for multi) and returns "
        + "them as a single reply. This tool does NOT wait for the answers: it poses the questions "
        + "and returns immediately. Only on success, STOP and end your turn; on failure, fix the input and retry; the user's answers "
        + "arrive as their next message, then continue.")]
    public static async Task<IToolResult> AskUser(
        RhinoDoc doc,
        [Description("The questions to ask, in the order they should be shown")] QuestionSpec[] questions)
    {
        Coercions coercions = new();
        IToolResult? failure = AskUserValidation.Validate(questions, coercions, out List<PendingQuestion> posed);
        if (failure is not null)
            return failure;

        // Attach to the live Conversation so the panel renders the cards; the Conversation is the
        // single source of truth for pending questions (it survives a panel dock/undock reload,
        // since the panel rebinds to the same live instance). AgentHost's dictionaries are
        // UI-thread-owned and unsynchronized; this tool body runs off the UI thread
        // ([BackgroundThread]), so resolve the Conversation on the UI thread where the TryFor read
        // can't race a New/SetActive mutation.
        ConversationLookup lookup = await ResolveConversationAsync(doc).ConfigureAwait(false);

        if (!lookup.Attached)
            return Failure(ToolError.Failed, "No active conversation is available to show the questions.",
                "Do not wait for answers: no questions were shown. Retry from an active Rhino AI panel conversation.");

        // A second ask_user APPENDS rather than replacing, so an earlier unanswered question is never
        // silently evicted, and the printed prompt below reports the whole outstanding set.
        IReadOnlyList<PendingQuestion> outstanding = posed;
        lookup.Conversation.AddPendingQuestions(posed);
        if (lookup.Conversation.TryGetPendingQuestions(out IReadOnlyList<PendingQuestion> all))
            outstanding = all;

        PrintPrompt(outstanding);

        // Non-blocking: the answers are not awaited here. The user answers in the panel and that
        // reply is dispatched as the agent's NEXT prompt, resuming the same live pooled agent.
        return Success(new
        {
            posed = posed.Count,
            outstanding = outstanding.Count,
            note = "Shown to the user in the Rhino panel. Stop now and end your turn; "
                + "the user's answers will be your next message, then continue.",
        }, coercions.Guidance);
    }

    private record struct ConversationLookup(bool Attached, Conversation Conversation);

    private static Task<ConversationLookup> ResolveConversationAsync(RhinoDoc doc)
    {
        TaskCompletionSource<ConversationLookup> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try { tcs.SetResult(ResolveConversation(doc)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }));
        return tcs.Task;
    }

    // UI-thread only: reads AgentHost's unsynchronized dictionaries.
    private static ConversationLookup ResolveConversation(RhinoDoc doc) =>
        AgentHost.TryFor(doc, out IAgentRunner agent)
            ? new ConversationLookup(true, agent.Conversation)
            : new ConversationLookup(false, default!);

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

        RhinoApp.WriteLine("  Answer in the AI panel.");
    }
}
