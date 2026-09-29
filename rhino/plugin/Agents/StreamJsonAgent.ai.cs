using System.Diagnostics;
using System.IO;

using System.Text.Json.Nodes;

using System.Threading;
using System.Threading.Tasks;

using Acp;
using ContentBlock = Acp.ContentBlock; // disambiguate from Rhino.AI.Server.ContentBlock

using Rhino.Runtime;

namespace Rhino.AI;

// The single owner of stream-json process lifecycle for any line-framed CLI agent (Claude, Codex).
// Owns the process spawn/resolve, the read loop, turn gating, error capture, Kill/respawn and
// Dispose; composes ONE IStreamJsonParser for all CLI-specific knowledge (launch args, turn framing,
// stdout translation). Plays the same IAcpAgent role native ACP agents do, so AgentRunner drives it
// identically. The runner reads AISettings here and hands the resolved MCP server set to the parser,
// keeping the parser pure (RhinoApp- and AISettings-free).
internal sealed class StreamJsonAgent : IAcpAgent, IDisposable
{
    private AgentDefinition Definition { get; }
    private IStreamJsonParser Parser { get; }
    private IAcpClient Client { get; }
    private Conversation Conversation { get; }
    private string Cwd { get; }

    private Process? Proc { get; set; }
    private StreamWriter? Stdin { get; set; }
    private Task? StartTask { get; set; }
    private TaskCompletionSource<StopReason>? CurrentTurn { get; set; }

    private object Gate { get; } = new();
    private SemaphoreSlim WriteGate { get; } = new(1, 1);
    private SemaphoreSlim TurnGate { get; } = new(1, 1);

    private record struct TurnCompletion(StopReason Reason, TokenUsage Usage);

    // Resolved by the read-loop exit, so a one-turn-per-process CLI cannot hand the next prompt a stdin it already closed.
    private TurnCompletion? PendingCompletion { get; set; }

    // Stable session id so a respawn (after cancel/crash) resumes the same CLI conversation. Seeded
    // from a resumed past conversation when one is supplied, so the first spawn continues that CLI
    // session rather than opening a brand-new one. Rotated to a fresh id only when a resume target is
    // rejected (see ReadLoopAsync), since Claude requires --session-id to be a never-used UUID and the
    // saved id was already consumed by the original session.
    private Guid AgentSessionId { get; set; }
    private string AgentSessionIdText => AgentSessionId.ToString();

    // Sticky: set true on the first successful spawn and NEVER reset by Kill or read-loop-exit (it is
    // the parser's 'resume' flag, so a respawn always continues with --resume rather than re-opening a
    // fresh session). Pre-seeded true when resuming a past conversation so even the FIRST spawn resumes.
    private bool HasEverStarted { get; set; }

    // True only while a resume-from-start spawn has not yet completed a single turn: the saved
    // --resume target is unproven, so a read-loop exit before any turn lands is treated as the CLI
    // rejecting the (likely expired) id. We then fail soft to a fresh session (see ReadLoopAsync).
    private bool ResumePending { get; set; }

    private string McpUrl { get; set; } = string.Empty;

    // The binary the last spawn resolved to, kept so the sign-in relaunch uses that exact CLI rather
    // than re-resolving (and possibly picking a different install).
    private string ExePath { get; set; } = string.Empty;

    // One browser sign-in at a time, cancelled when this agent is disposed.
    private bool LoginLaunched { get; set; }
    private CancellationTokenSource Lifetime { get; } = new();

    public StreamJsonAgent(AgentDefinition def, IAcpClient client, Conversation conversation, string cwd, IStreamJsonParser parser)
        : this(def, client, conversation, cwd, parser, resumeSessionId: null)
    {
    }

    // resumeSessionId carries a past conversation's CLI continuity token to continue it from the first
    // spawn; null opens a brand-new session with a fresh id.
    public StreamJsonAgent(AgentDefinition def, IAcpClient client, Conversation conversation, string cwd, IStreamJsonParser parser, Guid? resumeSessionId)
    {
        Definition = def;
        Parser = parser;
        Client = client;
        Conversation = conversation;
        Cwd = cwd;
        // The conversation's id IS the CLI session id. Generating a second one here is what broke
        // resume: the session was opened under this agent's private Guid while the transcript was
        // saved under the conversation's, so --resume named a session the CLI had never created.
        AgentSessionId = resumeSessionId ?? conversation.AgentSessionId;
        HasEverStarted = resumeSessionId is not null;   // first spawn resumes when a saved id is supplied
        ResumePending = resumeSessionId is not null;
    }

    public ValueTask<InitializeResponse> InitializeAsync(InitializeRequest request, CancellationToken cancellationToken = default) =>
        new(new InitializeResponse { ProtocolVersion = ProtocolConstants.Version });

    public ValueTask<NewSessionResponse> SessionNewAsync(NewSessionRequest request, CancellationToken cancellationToken = default)
    {
        foreach (Acp.McpServer server in request.McpServers)
            if (server is HttpMcpServer http && http.Name == "rhino")
                McpUrl = http.Url;
        return new(new NewSessionResponse { SessionId = AgentSessionIdText });
    }

    public async ValueTask<PromptResponse> SessionPromptAsync(PromptRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(request.Prompt).ConfigureAwait(false);

        await TurnGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TaskCompletionSource<StopReason> turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Gate)
                CurrentTurn = turn;

            await SendTurnAsync(request.Prompt).ConfigureAwait(false);
            StopReason reason = await turn.Task.ConfigureAwait(false);
            return new PromptResponse { StopReason = reason };
        }
        finally
        {
            lock (Gate)
                CurrentTurn = null;
            TurnGate.Release();
        }
    }

    // Resolve the in-flight turn as a clean cancel before tearing the process down, so a deliberate
    // cancel ends the turn with StopReason.Cancelled instead of faulting into the IOException the
    // read loop raises on exit (which AgentDispatch would print as a user-visible error line). This
    // wins the TCS first; the read loop's later TrySetException is then a no-op. The next prompt
    // respawns with --resume.
    public ValueTask SessionCancelAsync(CancelNotification notification, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<StopReason>? turn;
        lock (Gate)
        {
            turn = CurrentTurn;
            // A clean cancel is proof the user, not the CLI, ended the turn, so the resume target was
            // working: clear the unproven flag (leaving HasEverStarted true) so the read-loop exit
            // below doesn't misread the kill as a rejected --resume and falsely 'start fresh'.
            ResumePending = false;
        }
        turn?.TrySetResult(StopReason.Cancelled);
        Kill();
        return default;
    }

    public ValueTask<AuthenticateResponse> AuthenticateAsync(AuthenticateRequest request, CancellationToken cancellationToken = default) =>
        throw NotSupported("authenticate");

    public ValueTask<LoadSessionResponse> SessionLoadAsync(LoadSessionRequest request, CancellationToken cancellationToken = default) =>
        throw NotSupported("session/load");

    public ValueTask<SetSessionModeResponse> SessionSetModeAsync(SetSessionModeRequest request, CancellationToken cancellationToken = default) =>
        throw NotSupported("session/set_mode");

    public ValueTask<JsonElement> ExtMethodAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) =>
        throw NotSupported(method);

    public ValueTask ExtNotificationAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) => default;

    private AcpException NotSupported(string method) =>
        new($"'{method}' is not supported by the {Parser.DisplayName} agent", (int)Acp.JsonRpcErrorCode.MethodNotFound);

    // ---- launch + stdin -----------------------------------------------------------------------

    private Task EnsureStartedAsync(IReadOnlyList<ContentBlock> prompt)
    {
        lock (Gate)
            return StartTask ??= StartGuardedAsync(prompt);
    }

    private async Task StartGuardedAsync(IReadOnlyList<ContentBlock> prompt)
    {
        try
        {
            await StartAsync(prompt).ConfigureAwait(false);
        }
        catch (Exception)
        {
            lock (Gate)
                StartTask = null; // let the next prompt retry from scratch
            Kill();
            throw;
        }
    }

    private async Task StartAsync(IReadOnlyList<ContentBlock> prompt)
    {
        if (!CliProcess.TryResolve(Definition.SearchPaths.GetPaths(), out string path))
            throw new FileNotFoundException(Parser.NotFoundMessage);

        // Deliberately NOT probed before the spawn. `auth status` can report loggedIn:false for a CLI
        // that then works fine (credentials supplied by a host app rather than its own store), and a
        // pre-flight check that believed that would lock a working user out of the panel. Asked only
        // once a turn has already failed, the same wrong answer costs nothing but a stray window.
        lock (Gate)
            ExePath = path;

        ProcessStartInfo psi = new()
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Cwd,
        };
        CliProcess.ConfigureEncoding(psi);
        CliProcess.ConfigureFileName(psi, path);
        Parser.ConfigureArguments(psi, McpUrl, AgentSessionIdText, ResolveMcpServers(), HasEverStarted, prompt);

        Process proc = new() { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                HostUtils.LogDebugEvent($"[{Parser.DisplayName}:err] {e.Data}.\n");
        };
        proc.Start();
        proc.BeginErrorReadLine();

        // Publish the spawn under Gate: every reader of Proc (Kill, the read-loop exit guard,
        // CompleteTurn) takes the lock, so without a matching write-side barrier a UI-thread
        // Cancel()/Dispose()->Kill() racing the first spawn could read a stale null Proc, skip
        // proc.Kill(entireProcessTree:true) and orphan the CLI process tree.
        lock (Gate)
        {
            Proc = proc;
            Stdin = proc.StandardInput;
            Stdin.NewLine = "\n"; // stream-json input is newline-framed, not platform-framed
            HasEverStarted = true;
        }
        _ = Task.Run(() => ReadLoopAsync(proc.StandardOutput, proc));

    }

    // The runner resolves the MCP server set from AISettings (the parser stays settings-free). Each
    // list entry is a JSON-object string of server-name -> server-config that the parser merges in.
    // A bad textarea must never fault the launch, so this is fail-soft to an empty set.
    private IReadOnlyList<string> ResolveMcpServers()
    {
        string json;
        try
        {
            json = AISettings.ExtraMcpServersJson;
        }
        catch (Exception)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["mcpServers"] is not JsonObject inner || inner.Count == 0)
                return [];
            return [inner.ToJsonString(McpSerializer.Options)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task SendTurnAsync(IReadOnlyList<ContentBlock> prompt)
    {
        StreamWriter writer = Stdin ?? throw new InvalidOperationException($"{Parser.DisplayName} agent not started.");
        string line = Parser.FormatTurn(prompt);

        await WriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(line).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            if (Parser.IsOneTurnPerProcess)
            {
                writer.Close();
                lock (Gate)
                    Stdin = null;
            }
        }
        finally
        {
            WriteGate.Release();
        }
    }

    // ---- read + translate ---------------------------------------------------------------------

    // The read loop is process-agnostic: it reads lines from `stdout`, asks the parser to translate
    // each one, pushes the resulting SessionUpdates and resolves the turn on completion. `owner` is
    // the identity used to confirm a respawn has not replaced us (the Process in the real path, null
    // in the loopback seam); taking a TextReader rather than a Process is what lets RunLoopbackAsync
    // drive a scripted stdin/stdout pair with no real process.
    private async Task ReadLoopAsync(TextReader stdout, object? owner)
    {
        object? token = owner;
        try
        {
            string? line;
            while ((line = await stdout.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0)
                    continue;
                try
                {
                    ParsedLine parsed = Parser.Parse(line);
                    if (parsed.SessionId is string minted)
                        AdoptMintedSessionId(minted);
                    foreach (SessionUpdate update in parsed.Updates ?? [])
                        Push(update);
                    if (parsed.IsTurnComplete)
                        CompleteTurn(token, parsed.Reason, parsed.Usage);
                    // A turn the CLI itself calls failed is the one moment worth asking why: the
                    // answer is often an expired login. Asked AFTER the turn resolves, so the probe
                    // never sits between the user and their (already finished) answer.
                    if (parsed.IsTurnComplete && parsed.Reason == StopReason.Refusal)
                        await NoteIfSignedOutAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    HostUtils.LogDebugEvent($"[{Parser.DisplayName}] parse error: {ex.Message}.\n");
                }
            }
        }
        finally
        {
            // A CLI that exits mid-turn has either lost the resumed SESSION or lost the LOGIN, and
            // the two want opposite treatment (silently start fresh vs stop and sign in), so ask the
            // CLI which before deciding. Only asked when a turn is actually hanging on this exit: a
            // cancel or a teardown resolved its TCS already and pays nothing.
            bool hanging;
            lock (Gate)
                hanging = ReferenceEquals(Proc, token) && PendingCompletion is null && CurrentTurn is { Task.IsCompleted: false };
            string signedOut = hanging ? await SignedOutNoticeAsync().ConfigureAwait(false) : string.Empty;

            // Read-loop-exit ALWAYS faults the in-flight turn: a CLI that exits without emitting its
            // terminal event must never hang PromptAsync forever. A clean SessionCancelAsync has
            // already won the TCS by here, so this TrySetException is then a no-op.
            TaskCompletionSource<StopReason>? turn = null;
            bool resumeRejected = false;
            TurnCompletion? completed = null;
            lock (Gate)
            {
                // ReferenceEquals(null, null) is true, so the loopback seam (Proc and token both
                // null) takes this branch too; clearing the already-null Proc/Stdin is a no-op there.
                if (ReferenceEquals(Proc, token))
                {
                    turn = CurrentTurn;
                    completed = PendingCompletion;
                    PendingCompletion = null;
                    CurrentTurn = null;
                    Proc = null;
                    Stdin = null;
                    StartTask = null;

                    // An exit while the resume target is still unproven means the CLI rejected the
                    // (likely expired) --resume id. Fail soft: re-open fresh next prompt, keeping the
                    // restored transcript. HasEverStarted=false makes the next spawn pass --session-id,
                    // and the id is rotated to a fresh Guid because the saved one was already consumed
                    // by the original session (Claude rejects a reused --session-id as a collision).
                    // No turn has landed yet, so rotating the ACP-echoed id is safe (the runner already
                    // captured its own SessionId, and RhinoAcpClient routes by Conversation, not id).
                    // ...unless the CLI is simply signed out, which says nothing about whether the id
                    // was resumable: rotating it there would burn a perfectly good session.
                    if (ResumePending && signedOut.Length == 0)
                    {
                        resumeRejected = true;
                        ResumePending = false;
                        HasEverStarted = false;

                        // Keep the saved transcript pointing at the session that will actually
                        // exist, and drop the entry under the dead id: it is this same conversation,
                        // and leaving it behind puts an unresumable duplicate in the history.
                        Guid stale = Conversation.AgentSessionId;
                        AgentSessionId = Guid.NewGuid();
                        Conversation.AdoptSessionId(AgentSessionId);
                        ConversationStore.Delete(stale.ToString());
                    }
                }
            }
            if (resumeRejected)
                Conversation.NoteSystem("could not resume the saved session (it may have expired); started fresh");
            if (completed is TurnCompletion done)
            {
                Conversation.RecordUsage(done.Usage);
                turn?.TrySetResult(done.Reason);
            }
            else if (signedOut.Length > 0)
            {
                // "process exited" is useless when the CLI just told us why: the fix is a sign-in,
                // so it goes on the turn AND in the transcript.
                Conversation.NoteSystem(signedOut);
                turn?.TrySetException(new IOException(signedOut));
            }
            else
            {
                turn?.TrySetException(new IOException($"{Parser.DisplayName} process exited."));
            }
        }
    }

    // Ask the CLI whether the user is signed out, and start browser sign-in if so. Returns the line to
    // show, or "" when this was not a sign-in problem (signed in, or the CLI could not say) - the
    // caller then keeps whatever error it already had.
    private async Task<string> SignedOutNoticeAsync()
    {
        string path;
        lock (Gate)
            path = ExePath;

        CliLogin.State state = await CliLogin.ProbeAsync(path, Parser.AuthStatusArguments, Parser.ReadAuthState).ConfigureAwait(false);
        if (state != CliLogin.State.SignedOut)
            return string.Empty;

        return StartSignIn(path);
    }

    // The failed-turn path has no turn left to fault, so the transcript is the whole message.
    private async Task NoteIfSignedOutAsync()
    {
        string note = await SignedOutNoticeAsync().ConfigureAwait(false);
        if (note.Length > 0)
            Conversation.NoteSystem(note);
    }

    // Explicit sign-in also works before the first prompt, when no process has resolved ExePath.
    public void Login()
    {
        string path;
        lock (Gate)
            path = ExePath;
        if (path.Length == 0 && !CliProcess.TryResolve(Definition.SearchPaths.GetPaths(), out path))
        {
            Conversation.NoteSystem(Parser.NotFoundMessage);
            return;
        }
        Conversation.NoteSystem(StartSignIn(path));
    }

    private string StartSignIn(string path)
    {
        lock (Gate)
        {
            if (LoginLaunched)
                return $"Sign-in to {Parser.DisplayName} is already in progress. Finish signing in in your browser.";
            LoginLaunched = true;
        }

        _ = Task.Run(() => SignInAsync(path));
        return $"Opening your browser to sign in to {Parser.DisplayName}…";
    }

    private async Task SignInAsync(string path)
    {
        try
        {
            await CliLogin.SignInAsync(path, Parser.LoginArguments, Lifetime.Token).ConfigureAwait(false);
            if (!Lifetime.IsCancellationRequested)
                Conversation.NoteSystem($"Signed in to {Parser.DisplayName}. You can send your message now.");
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!Lifetime.IsCancellationRequested)
                Conversation.NoteSystem($"Could not sign in to {Parser.DisplayName}: {ex.Message} Try /login again.");
        }
        finally
        {
            lock (Gate)
                LoginLaunched = false;
        }
    }

    // The transcript has to be keyed by the id the CLI will actually resume, so adopt the minted one and drop the row we came in under.
    private void AdoptMintedSessionId(string minted)
    {
        if (!Guid.TryParse(minted, out Guid parsed))
            return;

        Guid stale;
        lock (Gate)
        {
            if (parsed == AgentSessionId)
                return;
            stale = AgentSessionId;
            AgentSessionId = parsed;
            ResumePending = false;
        }
        Conversation.AdoptSessionId(parsed);
        ConversationStore.Delete(stale.ToString());
    }

    // Our IAcpClient handler is synchronous, so this completes inline; the parser cloned any
    // JsonElements above so they outlive the parsed document.
    private void Push(SessionUpdate update) =>
        _ = Client.SessionUpdateAsync(new SessionNotification { SessionId = AgentSessionIdText, Update = update });

    private void CompleteTurn(object? token, StopReason reason, TokenUsage usage)
    {
        TaskCompletionSource<StopReason>? turn = null;
        lock (Gate)
            if (ReferenceEquals(Proc, token)) // both null in the loopback seam, so it matches there
            {
                turn = CurrentTurn;
                ResumePending = false; // a turn landed, so the --resume target was accepted
                if (Parser.IsOneTurnPerProcess)
                {
                    PendingCompletion = new TurnCompletion(reason, usage);
                    return;
                }
            }
        if (turn is null)
            return;
        // Record usage onto the live turn BEFORE resolving the TCS: the runner's PromptAsync finally
        // clears Current via CompleteTurn the moment this result lands, so usage must reach the turn
        // while it is still Current.
        Conversation.RecordUsage(usage);
        turn.TrySetResult(reason);
    }

    private void Kill()
    {
        Process? proc;
        lock (Gate)
        {
            proc = Proc;
            StartTask = null;
        }
        try
        {
            if (proc is { HasExited: false })
                proc.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // process already gone
        }
    }

    public void Dispose()
    {
        // Inert when never started: Kill is a no-op while Proc is null (probe-then-dispose in the
        // AgentHost pool must not spawn or tear down anything).
        Kill();
        Lifetime.Cancel(); // ...and stop browser sign-in before it posts into a replaced conversation
        TaskCompletionSource<StopReason>? turn;
        lock (Gate)
            turn = CurrentTurn;
        turn?.TrySetException(new ObjectDisposedException(nameof(StreamJsonAgent)));
        // WriteGate/TurnGate are deliberately not disposed: the faulted turn above still runs its
        // finally (TurnGate.Release), and disposing here would race it into ObjectDisposedException
        // ("System.Threading.SemaphoreSlim").
    }

    // ---- testability seam ---------------------------------------------------------------------

    // Drive the read loop over an injected stdin/stdout pair instead of a real Process, so a loopback
    // test can script one turn (write the framed prompt, replay canned stdout lines) and assert the
    // SessionUpdates reach the client and the turn resolves. No process is spawned; the loop's owner
    // token is null so every Push/CompleteTurn applies. The caller writes to `stdin` to script input.
    internal async Task<StopReason> RunLoopbackAsync(TextReader stdout, TextWriter stdin, IReadOnlyList<ContentBlock> prompt)
    {
        TaskCompletionSource<StopReason> turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
            CurrentTurn = turn;

        await stdin.WriteLineAsync(Parser.FormatTurn(prompt)).ConfigureAwait(false);
        await stdin.FlushAsync().ConfigureAwait(false);

        Task loop = ReadLoopAsync(stdout, owner: null);
        StopReason reason = await turn.Task.ConfigureAwait(false);
        await loop.ConfigureAwait(false);
        return reason;
    }
}
