using System.Threading.Tasks;

namespace RhMcp.Tools;

[McpServerToolType]
public static class AskUserTool
{
    [McpServerTool("ask_user", "Ask User", true, false)]
    [BackgroundThread]
    [InPanelOnly]
    [Description("Ask the Rhino user to choose among options when you need a decision you cannot "
        + "make yourself. The question and options appear inline in the Rhino MCP panel (radio for "
        + "single choice, checkboxes for multi) and on the command line. This tool does NOT wait for "
        + "the answer: it poses the question and returns immediately. STOP and end your turn after "
        + "calling it; the user's answer arrives as their next message, then continue.")]
    public static async Task<object> AskUser(
        RhinoDoc doc,
        [Description("The question to show the user")] string question,
        [Description("The options to choose from")] string[] options,
        [Description("true = user may select multiple options (checkboxes); "
            + "false = single choice (radio). Default false.")] bool multiSelect = false)
    {
        options ??= [];
        if (options.Length == 0)
            return "ask_user requires at least one option.";

        AskUserMode mode = multiSelect ? AskUserMode.Multi : AskUserMode.Single;
        PendingQuestion pending = new(question, options, mode);

        // The constructor collapses a literal "Other", so an options:["Other"] array survives the
        // raw length guard above yet leaves no real options. Reject it the same way as an empty array.
        if (pending.Options.Count == 0)
            return "ask_user requires at least one option.";

        uint docSerial = doc.RuntimeSerialNumber;

        // Attach to the live Conversation so the panel renders the card; the Conversation is the
        // single source of truth for the pending question (it survives a panel dock/undock reload,
        // since the panel rebinds to the same live instance). AgentHost's dictionaries are
        // UI-thread-owned and unsynchronized; this tool body runs off the UI thread
        // ([BackgroundThread]), so resolve the Conversation on the UI thread where the TryFor read
        // can't race a New/SetActive mutation.
        ConversationLookup lookup = await ResolveConversationAsync(doc).ConfigureAwait(false);
        if (lookup.Attached)
            lookup.Conversation.SetPendingQuestion(pending);

        // Present the command-line GetOption picker on the UI thread as an answer affordance (the
        // panel card is the other channel). Picking dispatches the choice as the next prompt and
        // clears the card; it degrades to the printed prompt when the Get can't run (mid-command, or
        // a platform that rejects an out-of-command Get). Print the prompt FIRST so the fallback text
        // is always on the command line whether or not the picker takes over.
        PrintPrompt(pending);
        ShowPicker(docSerial, pending);

        // Non-blocking: the answer is not awaited here. The user picks in the panel (or on the
        // command line) and that choice is dispatched as the agent's NEXT prompt, resuming the same
        // live pooled agent. Return now and steer the agent to end its turn.
        return new
        {
            posed = true,
            note = "Shown to the user in the Rhino panel. Stop now and end your turn; "
                + "the user's answer will be your next message, then continue.",
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
    // picker's modal Get runs on the UI thread until the user picks, the panel answers (Cancel), or a
    // superseding ask_user cancels it; the tool has already returned by then, so we never await it.
    private static void ShowPicker(uint docSerial, PendingQuestion pending) =>
        RhinoApp.InvokeOnUiThread(new Action(() => AskUserPicker.TryShow(docSerial, pending)), null);

    private static void PrintPrompt(PendingQuestion pending)
    {
        RhinoApp.WriteLine($"[ask] {pending.Question}");
        for (int i = 0; i < pending.Options.Count; i++)
            RhinoApp.WriteLine($"  {i + 1}. {pending.Options[i]}");

        string hint = pending.Mode == AskUserMode.Multi
            ? "Answer in the panel, or type \"<numbers or labels, comma-separated>\"."
            : "Answer in the panel, or type \"<number or label>\".";
        RhinoApp.WriteLine($"  {hint}");
    }
}
