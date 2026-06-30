using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp;

// The single funnel every surface (command, command-line interceptor, panel) routes through,
// so they all drive one active agent per doc against one shared conversation. Resolves the
// active agent, ensures an MCP listener for the doc, then fires the turn off-thread.
internal static class AgentDispatch
{
    // One slot per doc, taken for the full Open..run..Close of a turn. It serializes the undo-record
    // bracket itself (BeginUndoRecord must never run while another turn's record is open, or Rhino
    // declines it and that turn's mutations land in the wrong record), and it is the funnel-level
    // one-turn-at-a-time guard the panel's Send/Stop button only enforces at the UI: the interceptor
    // and AgentCommand reach PromptActive directly, so the guard has to live here, not on the button.
    private static Dictionary<uint, SemaphoreSlim> DocGates { get; } = new();

    // At most one held ask_user answer per doc. ask_user is non-blocking (W11): the question card and
    // the command-line GetOption picker both show DURING the agent's still-running turn, so the user
    // usually answers before the turn ends. That answer can't dispatch onto a busy gate, so it is
    // parked here and flushed the instant the turn's finally releases the gate. The pending slot holds
    // exactly one answer (a later answer replaces an earlier one) and is cleared only on a dispatch
    // that actually acquired the gate, so an answer is never silently dropped onto a busy gate.
    private static Dictionary<uint, UserMessage> PendingAnswers { get; } = new();
    private static object PendingLock { get; } = new();

    static AgentDispatch()
    {
        // Forget a closed doc's gate so the map doesn't accumulate one slot per doc opened this
        // session. Not disposed: an in-flight turn's finally still runs gate.Release() on the same
        // object reference, and disposing would race it into ObjectDisposedException.
        RhinoDoc.CloseDocument += (_, e) =>
        {
            lock (DocGates)
                DocGates.Remove(e.DocumentSerialNumber);
            lock (PendingLock)
                PendingAnswers.Remove(e.DocumentSerialNumber);
        };
    }

    private static SemaphoreSlim GateFor(uint docSerial)
    {
        lock (DocGates)
        {
            if (!DocGates.TryGetValue(docSerial, out SemaphoreSlim? gate))
            {
                gate = new SemaphoreSlim(1, 1);
                DocGates[docSerial] = gate;
            }
            return gate;
        }
    }

    // The agent needs an MCP listener as its hands; auto-start one for this doc if absent so the
    // happy path needs zero setup. Shared by panel-open (warm the listener the moment an agent is
    // available) and the prompt path (the safety net if open didn't run). Idempotent: a started
    // listener is reused. Worked-or-not so callers can stay silent on the warm-up path.
    public static bool TryEnsureListener(RhinoDoc doc, out int port)
    {
        if (RhinoMcpHost.TryGetPortFor(doc, out port))
            return true;

        if (!RhinoMcpHost.TryGetNextPort(out int nextPort))
            return false;

        RhinoMcpHost.StartOrRestart(doc, nextPort);
        return RhinoMcpHost.TryGetPortFor(doc, out port);
    }

    // A FRESH user prompt (AgentCommand / CommandInterceptor / panel Send). Returns whether the turn
    // was accepted (queued to run). A false return means nothing started: no agent, no listener, or a
    // turn is already running for this doc. Announces the busy gate so the user knows their prompt was
    // declined rather than silently dropped.
    public static bool PromptActive(RhinoDoc doc, UserMessage message) =>
        TryDispatch(doc, message, asAnswer: false, announceBusy: true);

    // The entry point for ask_user ANSWERS (panel card or command-line picker). The answer can land
    // while the agent's turn is still running, so it is PARKED as this doc's pending answer (replacing
    // any prior) and then flushed: dispatched now if the gate is free, otherwise held and flushed the
    // instant the running turn's finally releases the gate. The answer is never rejected/lost: once
    // parked it is guaranteed to be delivered, so the caller can clear the question card on the
    // winning claim. Callable from the UI thread (panel/picker); the flush marshals its real dispatch
    // onto the UI thread itself.
    public static void AnswerActive(RhinoDoc doc, UserMessage message)
    {
        lock (PendingLock)
            PendingAnswers[doc.RuntimeSerialNumber] = message;
        FlushPendingAnswer(doc);
    }

    // Dispatch this doc's held answer (if any) onto a free gate; quiet on a busy gate so a held answer
    // makes no '[name] a turn is already running' noise (it stays pending and flushes on turn
    // completion). The pending slot is consumed ATOMICALLY with the gate acquisition (TryDispatch
    // removes it in the same critical section that wins Wait(0)), so a busy gate leaves the answer
    // queued for the next release and the slot is never read-then-stale-cleared.
    //
    // THREADING: TryDispatch reads AgentHost's UI-thread-owned dicts, and this is callable from the
    // background gate-release continuation, so the dispatch runs on the UI thread. Posting it (rather
    // than running inline) also serializes flushes against the panel/picker callers, which are already
    // on the UI thread.
    public static void FlushPendingAnswer(RhinoDoc doc) =>
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            UserMessage message;
            lock (PendingLock)
            {
                if (!PendingAnswers.TryGetValue(doc.RuntimeSerialNumber, out UserMessage? held))
                    return;
                message = held;
            }

            TryDispatch(doc, message, asAnswer: true, announceBusy: false);
        }), null);

    // The shared acquire-or-reject path behind both a fresh prompt and an answer flush. Resolves the
    // agent + listener, then makes the gate decision and the pending-slot bookkeeping in ONE critical
    // section under PendingLock so they are atomic with each other:
    //
    //   asAnswer:false (fresh prompt), a parked answer for this doc has PRIORITY: reject the fresh
    //     prompt (announce busy) so the answer flushes first and the conversation isn't reordered (the
    //     agent must see [answer to Q] before any unrelated newer prompt). Otherwise try Wait(0): free
    //     fires the turn, busy announces 'a turn is already running'.
    //
    //   asAnswer:true (answer flush), try Wait(0): on a win, REMOVE the pending slot in the same
    //     locked section that won the gate (atomic consume-and-fire, so the slot is never read here and
    //     stale-cleared after RunTurnAsync, and never double-dispatched), then fire. A busy gate leaves
    //     the answer parked and is silent (it flushes on the next turn completion).
    //
    // A busy gate is never otherwise queued (or two users' intent would merge into one undo record and
    // one session); the at-most-one parked answer is the only out-of-band queue, and it is drained
    // ahead of fresh prompts.
    private static bool TryDispatch(RhinoDoc doc, UserMessage message, bool asAnswer, bool announceBusy)
    {
        if (!AgentHost.TryFor(doc, out IAgentRunner agent))
        {
            RhinoApp.WriteLine("No agent available. Open AI Settings to configure one.");
            return false;
        }

        if (!TryEnsureListener(doc, out int port))
        {
            RhinoApp.WriteLine($"[{agent.Name}] could not start an MCP server for this document.");
            return false;
        }

        string url = $"http://localhost:{port}/agent";
        string cwd = !string.IsNullOrEmpty(doc.Path)
            ? Path.GetDirectoryName(doc.Path) ?? Path.GetTempPath()
            : Path.GetTempPath();

        // Resolve the gate (locks DocGates briefly) BEFORE taking PendingLock so there is no nested
        // lock-order inversion: the gate decision below holds only PendingLock.
        uint docSerial = doc.RuntimeSerialNumber;
        SemaphoreSlim gate = GateFor(docSerial);

        lock (PendingLock)
        {
            // Parked-answer priority: a fresh prompt yields to a held answer for this doc so the answer
            // (to a question the agent already posed) is never delivered after an unrelated new prompt.
            if (!asAnswer && PendingAnswers.ContainsKey(docSerial))
            {
                if (announceBusy)
                    RhinoApp.WriteLine($"[{agent.Name}] a turn is already running for this document. Wait for it to finish or Stop it.");
                return false;
            }

            if (!gate.Wait(0))
            {
                if (announceBusy)
                    RhinoApp.WriteLine($"[{agent.Name}] a turn is already running for this document. Wait for it to finish or Stop it.");
                return false;
            }

            // Won the gate. For an answer flush, consume the parked slot in this SAME locked section so
            // a posted second flush can't read it again (the slot is gone before RunTurnAsync fires).
            if (asAnswer)
                PendingAnswers.Remove(docSerial);
        }

        // Fire-and-forget, but never unobserved: a fault from Open/Close/prompt is surfaced here so a
        // dropped turn is visible rather than swallowed by a discarded Task.
        _ = RunTurnAsync(agent, doc, message, url, cwd, gate)
            .ContinueWith(
                t => RhinoApp.WriteLine($"[{agent.Name}] turn faulted: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        return true;
    }

    // Bracket the whole turn in one undo record so a single Ctrl+Z reverts every mutation it made.
    // The doc gate is already held (acquired in TryDispatch), so the record is opened only when no
    // other turn's record is open. Open before the prompt fires (the first tool call must land inside
    // the record); close in the finally so it is ALWAYS ended, whether the turn completes, is
    // cancelled, or faults, and release the gate in the same finally so Open..Close is atomic.
    private static async Task RunTurnAsync(IAgentRunner agent, RhinoDoc doc, UserMessage message, string url, string cwd, SemaphoreSlim gate)
    {
        TurnUndoCheckpoint? checkpoint = null;
        try
        {
            checkpoint = await TurnUndoCheckpoint.OpenAsync(doc, TurnUndoCheckpoint.Describe(message.Text)).ConfigureAwait(false);
            await agent.PromptAsync(message, url, cwd).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Agent torn down mid-turn (New conversation / Stop): expected, not an error.
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[{agent.Name}] error: {ex.Message}");
        }
        finally
        {
            if (checkpoint is not null)
                await checkpoint.CloseAsync().ConfigureAwait(false);
            gate.Release();

            // The gate is now free. An ask_user answer parked during this turn (the user answered the
            // card/picker while the turn was still running) flushes here, the instant the turn ends.
            // FlushPendingAnswer marshals its dispatch onto the UI thread, so calling it from this
            // background continuation is safe.
            FlushPendingAnswer(doc);
        }
    }
}
