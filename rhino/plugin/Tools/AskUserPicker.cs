using System.Text;
using System.Threading;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace RhMcp;

// Command-line answer affordance for a posed ask_user question, restored from the old blocking
// picker but adapted to the non-blocking model. The ask_user tool has ALREADY returned, so this
// GetOption does not wait for or carry a result: it merely lets the user COMPOSE the answer on the
// command line. Picking an option dispatches that choice as the agent's next prompt (exactly like a
// panel click) and clears the pending question. It runs on the UI thread, alongside the panel card.
//
// First-wins with the panel: the modal Get holds the UI thread, so a panel click can only land
// between poll ticks (SetWaitDuration wakes Get periodically and lets the message pump drain). Both
// channels funnel their answer through the SAME Interlocked claim on the running picker: the picker
// picks an option and calls AnswerPicked; the panel calls TryClaim first and only dispatches if it
// wins. Whoever flips Claimed 0->1 is the single dispatcher; the loser is a no-op. The picker also
// re-checks Cancelled immediately after Get returns so a panel click pumped mid-Get can't trigger a
// second dispatch. When no picker is running for a question (it never started, or already unwound),
// the panel is the only channel and TryClaim succeeds unconditionally.
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

        internal Running(PendingQuestion question) => Question = question;

        internal PendingQuestion Question { get; }

        internal bool Cancelled => CancelledFlag;
        internal void Cancel() => CancelledFlag = true;

        // 0 = unanswered, 1 = claimed. Stays a field (not a property) because Interlocked.Exchange
        // needs a ref to it; the claim is the single funnel both channels flip exactly once.
        internal int Claimed;
    }

    // Present the command-line picker for a freshly posed question. MUST be called on the UI thread
    // (Rhino Get APIs are UI-thread only). Returns worked-or-not so the caller keeps the printed
    // prompt as the fallback when the Get cannot run here.
    public static bool TryShow(uint docSerial, PendingQuestion question)
    {
        // A Get nested inside another command's input loop is unsafe; let the printed prompt stand.
        if (Command.InCommand())
            return false;

        // A newer ask_user supersedes any picker still running for this doc: cancel the old one so it
        // unwinds on its next poll instead of two pickers fighting for the command line.
        Running running = new(question);
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

    // Signal a running picker for this exact question to abort (the panel dismissed it, or a newer
    // ask_user superseded it). ReferenceEquals-guarded so a stale clear can't cancel a newer picker.
    public static void Cancel(uint docSerial, PendingQuestion question)
    {
        lock (Gate)
            if (Active.TryGetValue(docSerial, out Running? current) && ReferenceEquals(current.Question, question))
                current.Cancel();
    }

    // The panel's entry into the same single claim the picker uses. Flips the running picker's
    // Interlocked claim for THIS question and, on a win, cancels it so its loop unwinds without a
    // second dispatch. No running picker for the question (it never started or already unwound)
    // means the panel is the only channel, so the claim succeeds unconditionally. Returns whether
    // the caller won the right to dispatch the answer.
    public static bool TryClaim(uint docSerial, PendingQuestion question)
    {
        lock (Gate)
        {
            if (!Active.TryGetValue(docSerial, out Running? current) || !ReferenceEquals(current.Question, question))
                return true;
            if (Interlocked.Exchange(ref current.Claimed, 1) != 0)
                return false;
            current.Cancel();
            return true;
        }
    }

    private static void Run(uint docSerial, Running running)
    {
        if (running.Question.Mode == AskUserMode.Multi)
            RunMulti(docSerial, running);
        else
            RunSingle(docSerial, running);
    }

    private static void RunSingle(uint docSerial, Running running)
    {
        PendingQuestion question = running.Question;
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
                return;   // Cancel / Escape / unexpected: leave the question for the panel.

            // A panel click pumped mid-Get may have cancelled (and claimed) this picker; re-check
            // before dispatching so the two channels can't both answer.
            if (running.Cancelled)
                return;

            int index = go.Option().Index;
            if (index == otherIndex)
            {
                if (AskOther() is string typed)
                    AnswerPicked(docSerial, running, typed);
                return;
            }
            if (byIndex.TryGetValue(index, out string? chosen))
            {
                AnswerPicked(docSerial, running, chosen);
                return;
            }
        }
    }

    // Multi-select on the command line: each option is an On/Off toggle; Done commits the set,
    // Other appends a typed answer. The toggles live for the whole loop so the user builds up a
    // selection before committing, mirroring the panel checkboxes.
    private static void RunMulti(uint docSerial, Running running)
    {
        PendingQuestion question = running.Question;
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
                return;

            int index = go.Option().Index;
            if (index == doneIndex)
                break;
            if (index == otherIndex && AskOther() is string typed)
                custom.Add(typed);
        }

        if (running.Cancelled)
            return;

        List<string> selected = [];
        foreach ((string label, string _, OptionToggle toggle) in items)
            if (toggle.CurrentValue)
                selected.Add(label);
        selected.AddRange(custom);
        if (selected.Count == 0)
            return;   // Done with nothing picked: behave like Cancel, leave it to the panel.
        AnswerPicked(docSerial, running, string.Join(", ", selected));
    }

    // The first-wins claim shared with the panel: flip Claimed exactly once, then park the answer as
    // the agent's next prompt. AnswerActive guarantees delivery (dispatched now if the gate is free,
    // otherwise held and flushed the instant the running turn ends), so the answer is never lost and
    // the live conversation's question is cleared unconditionally once parked.
    private static void AnswerPicked(uint docSerial, Running running, string answer)
    {
        if (Interlocked.Exchange(ref running.Claimed, 1) != 0)
            return;

        // The doc could have closed between posing and picking; if so there is nothing to answer to.
        if (RhinoDoc.FromRuntimeSerialNumber(docSerial) is not { } doc)
            return;

        AgentDispatch.AnswerActive(doc, UserMessage.FromText(answer));
        if (AgentHost.TryFor(doc, out IAgentRunner agent))
            agent.Conversation.ClearPendingQuestion(running.Question);
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
