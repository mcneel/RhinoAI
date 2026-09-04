using System.Text;
using System.Threading;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace RhinoAI;

// Command-line answer affordance for posed ask_user questions, restored from the old blocking picker
// but adapted to the non-blocking model. The ask_user tool has ALREADY returned, so this GetOption
// does not wait for or carry a result: it merely lets the user COMPOSE the answer on the command
// line. Working through the questions dispatches the answers as the agent's next prompt (exactly
// like a panel submit) and clears the pending questions. It runs on the UI thread, alongside the
// panel card.
//
// One picker per doc walks the WHOLE outstanding set in order and dispatches once at the end, so the
// command line and the panel produce the same single reply. A newer ask_user cancels the running
// picker and restarts it over the full set (the appended question has to be visible); that discards
// toggles already set on the command line, which is the price of keeping one picker per doc.
//
// First-wins with the panel: the modal Get holds the UI thread, so a panel submit can only land
// between poll ticks (SetWaitDuration wakes Get periodically and lets the message pump drain). Both
// channels funnel through the SAME Interlocked claim on the running picker: the picker finishes its
// walk and calls AnswerPicked; the panel calls TryClaim first and only dispatches if it wins.
// Whoever flips Claimed 0->1 is the single dispatcher; the loser is a no-op. The picker also
// re-checks Cancelled immediately after Get returns so a panel submit pumped mid-Get can't trigger a
// second dispatch. When no picker is running (it never started, or already unwound), the panel is
// the only channel and TryClaim succeeds unconditionally.
//
// CROSS-PLATFORM RISK: a Get fired outside a running command is fragile and behaves differently on
// Windows vs Mac (on Rhino 8 Mac some Get paths misbehave, see AgentCommand). Everything here is
// guarded and degrades to the printed prompt if the Get cannot start; the live Mac + Windows
// behaviour of this out-of-command GetOption still needs manual verification.
internal static class AskUserPicker
{
    private static object Gate { get; } = new();
    private static Dictionary<uint, Running> Active { get; } = new();

    private const int PollMilliseconds = 150;

    // Toggle labels reserved alongside the synthesized free-text and finish options so a real option
    // can never collide with them.
    private const string OtherToken = "Other";
    private const string DoneToken = "Done";

    private sealed class Running
    {
        // Set from any thread (panel UI thread, or a superseding ask_user on the MCP background
        // thread); read by the polling Get loop on the UI thread, so it is volatile.
        private volatile bool CancelledFlag;

        internal Running(IReadOnlyList<PendingQuestion> questions) => Questions = questions;

        internal IReadOnlyList<PendingQuestion> Questions { get; }

        internal bool Cancelled => CancelledFlag;
        internal void Cancel() => CancelledFlag = true;

        // 0 = unanswered, 1 = claimed. Stays a field (not a property) because Interlocked.Exchange
        // needs a ref to it; the claim is the single funnel both channels flip exactly once.
        internal int Claimed;

        internal bool Covers(IReadOnlyList<PendingQuestion> questions)
        {
            foreach (PendingQuestion question in questions)
                foreach (PendingQuestion mine in Questions)
                    if (ReferenceEquals(mine, question))
                        return true;
            return false;
        }
    }

    // Present the command-line picker for the outstanding questions. MUST be called on the UI thread
    // (Rhino Get APIs are UI-thread only). Returns worked-or-not so the caller keeps the printed
    // prompt as the fallback when the Get cannot run here.
    public static bool TryShow(uint docSerial, IReadOnlyList<PendingQuestion> questions)
    {
        // A Get nested inside another command's input loop is unsafe; let the printed prompt stand.
        if (Command.InCommand())
            return false;
        if (questions.Count == 0)
            return false;

        // A newer ask_user supersedes any picker still running for this doc: cancel the old one so it
        // unwinds on its next poll instead of two pickers fighting for the command line.
        Running running = new(questions);
        lock (Gate)
        {
            if (Active.TryGetValue(docSerial, out Running? prior))
                prior.Cancel();
            Active[docSerial] = running;
        }

        try
        {
            Run(docSerial, running);
            return true;
        }
        catch (Exception ex)
        {
            // Out-of-command Get is cross-platform fragile; degrade to the printed prompt rather
            // than fault the idle/dispatch path.
            RhinoApp.WriteLine($"[ask] command-line picker unavailable ({ex.Message}); answer in the panel or type your reply.");
            return false;
        }
        finally
        {
            lock (Gate)
                if (Active.TryGetValue(docSerial, out Running? current) && ReferenceEquals(current, running))
                    Active.Remove(docSerial);
        }
    }

    // Signal a running picker covering these questions to abort (the panel dismissed them, or a newer
    // ask_user superseded it). Instance-guarded so a stale clear can't cancel a newer picker.
    public static void Cancel(uint docSerial, IReadOnlyList<PendingQuestion> questions)
    {
        lock (Gate)
            if (Active.TryGetValue(docSerial, out Running? current) && current.Covers(questions))
                current.Cancel();
    }

    // The panel's entry into the same single claim the picker uses. Flips the running picker's
    // Interlocked claim and, on a win, cancels it so its loop unwinds without a second dispatch. No
    // running picker covering these questions (it never started or already unwound) means the panel
    // is the only channel, so the claim succeeds unconditionally. Returns whether the caller won the
    // right to dispatch the answer.
    public static bool TryClaim(uint docSerial, IReadOnlyList<PendingQuestion> questions)
    {
        lock (Gate)
        {
            if (!Active.TryGetValue(docSerial, out Running? current) || !current.Covers(questions))
                return true;
            if (Interlocked.Exchange(ref current.Claimed, 1) != 0)
                return false;
            current.Cancel();
            return true;
        }
    }

    // Walk every outstanding question in order, then dispatch ONE reply. An abandoned question
    // (Escape / Cancel) ends the walk and leaves the whole set to the panel: a partial command-line
    // reply would answer some questions and silently drop the rest.
    private static void Run(uint docSerial, Running running)
    {
        List<string> answers = [];
        foreach (PendingQuestion question in running.Questions)
        {
            if (running.Cancelled)
                return;

            string? answer = question.Mode == AskUserMode.Multi
                ? RunMulti(running, question)
                : RunSingle(running, question);
            if (answer is null)
                return;

            answers.Add(answer);
        }

        if (running.Cancelled || answers.Count == 0)
            return;
        AnswerPicked(docSerial, running, Compose(running.Questions, answers));
    }

    // The reply text both channels produce. A single question keeps the bare answer it always had;
    // a batch labels each line so the agent can map answers back to the questions it asked.
    public static string Compose(IReadOnlyList<PendingQuestion> questions, IReadOnlyList<string> answers)
    {
        if (questions.Count == 1)
            return answers.Count > 0 ? answers[0] : string.Empty;

        StringBuilder sb = new();
        for (int i = 0; i < questions.Count && i < answers.Count; i++)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(questions[i].Question).Append(' ').Append(answers[i]);
        }
        return sb.ToString();
    }

    // Returns the chosen label, or null when the user abandoned the question.
    private static string? RunSingle(Running running, PendingQuestion question)
    {
        GetOption go = new();
        go.SetCommandPrompt(question.Question);
        go.SetWaitDuration(PollMilliseconds);

        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase) { OtherToken };
        Dictionary<int, string> byIndex = [];
        foreach (string label in question.Options)
        {
            string token = ToToken(label, used);
            byIndex[go.AddOption(token)] = label;
            PrintMapping(token, label);
        }
        int otherIndex = go.AddOption(OtherToken);

        while (!running.Cancelled)
        {
            GetResult res = go.Get();
            if (res == GetResult.Timeout)
                continue;
            if (res != GetResult.Option)
                return null;   // Cancel / Escape / unexpected: leave the question for the panel.

            // A panel submit pumped mid-Get may have cancelled (and claimed) this picker; re-check
            // before going on so the two channels can't both answer.
            if (running.Cancelled)
                return null;

            int index = go.Option().Index;
            if (index == otherIndex)
                return AskOther();
            if (byIndex.TryGetValue(index, out string? chosen))
                return chosen;
        }
        return null;
    }

    // Multi-select on the command line: each option is an On/Off toggle; Done commits the set,
    // Other appends a typed answer. The toggles live for the whole loop so the user builds up a
    // selection before committing, mirroring the panel checkboxes.
    private static string? RunMulti(Running running, PendingQuestion question)
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase) { OtherToken, DoneToken };
        List<(string Label, string Token, OptionToggle Toggle)> items = [];
        foreach (string label in question.Options)
        {
            string token = ToToken(label, used);
            items.Add((label, token, new OptionToggle(false, "Off", "On")));
            PrintMapping(token, label);
        }
        List<string> custom = [];

        while (!running.Cancelled)
        {
            GetOption go = new();
            go.SetCommandPrompt(question.Question);
            go.SetWaitDuration(PollMilliseconds);
            for (int i = 0; i < items.Count; i++)
            {
                OptionToggle toggle = items[i].Toggle;
                go.AddOptionToggle(items[i].Token, ref toggle);
            }
            int otherIndex = go.AddOption(OtherToken);
            int doneIndex = go.AddOption(DoneToken);

            GetResult res = go.Get();
            if (res == GetResult.Timeout)
                continue;
            if (res != GetResult.Option)
                return null;

            int index = go.Option().Index;
            if (index == doneIndex)
                break;
            if (index == otherIndex && AskOther() is string typed)
                custom.Add(typed);
        }

        if (running.Cancelled)
            return null;

        List<string> selected = [];
        foreach ((string label, string _, OptionToggle toggle) in items)
            if (toggle.CurrentValue)
                selected.Add(label);
        selected.AddRange(custom);
        if (selected.Count == 0)
            return null;   // Done with nothing picked: behave like Cancel, leave it to the panel.
        return string.Join(", ", selected);
    }

    // The first-wins claim shared with the panel: flip Claimed exactly once, then park the answer as
    // the agent's next prompt. AnswerActive guarantees delivery (dispatched now if the gate is free,
    // otherwise held and flushed the instant the running turn ends), so the answer is never lost and
    // the live conversation's questions are cleared unconditionally once parked.
    private static void AnswerPicked(uint docSerial, Running running, string answer)
    {
        if (Interlocked.Exchange(ref running.Claimed, 1) != 0)
            return;

        // The doc could have closed between posing and picking; if so there is nothing to answer to.
        if (RhinoDoc.FromRuntimeSerialNumber(docSerial) is not { } doc)
            return;

        AgentDispatch.AnswerActive(doc, UserMessage.FromText(answer));
        if (AgentHost.TryFor(doc, out IAgentRunner agent))
            agent.Conversation.ClearPendingQuestions(running.Questions);
    }

    // Literal capture so a multi-word answer survives verbatim.
    private static string? AskOther()
    {
        GetString get = new();
        get.SetCommandPrompt("Type your answer");
        return get.GetLiteralString() == GetResult.String && !string.IsNullOrWhiteSpace(get.StringResult())
            ? get.StringResult().Trim()
            : null;
    }

    private static void PrintMapping(string token, string label)
    {
        if (!string.Equals(token, label, StringComparison.Ordinal))
            RhinoApp.WriteLine($"  {token} = {label}");
    }

    // Rhino option names must be CamelCase letters/digits starting with a letter, and unique.
    private static string ToToken(string label, HashSet<string> used)
    {
        StringBuilder sb = new();
        bool newWord = true;
        foreach (char c in label)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(newWord ? char.ToUpperInvariant(c) : c);
                newWord = false;
            }
            else
            {
                newWord = true;
            }
        }

        string token = sb.Length > 0 && char.IsLetter(sb[0]) ? sb.ToString() : "Opt" + sb;
        string candidate = token;
        int n = 2;
        while (!used.Add(candidate))
            candidate = $"{token}{n++}";
        return candidate;
    }
}
