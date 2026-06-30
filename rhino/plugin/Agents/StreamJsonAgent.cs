using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Acp;
using ContentBlock = Acp.ContentBlock; // disambiguate from RhMcp.Server.ContentBlock

namespace RhMcp;

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
        AgentSessionId = resumeSessionId ?? Guid.NewGuid();
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
        await EnsureStartedAsync().ConfigureAwait(false);

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

    private Task EnsureStartedAsync()
    {
        lock (Gate)
            return StartTask ??= StartGuardedAsync();
    }

    private async Task StartGuardedAsync()
    {
        try
        {
            await StartAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            lock (Gate)
                StartTask = null; // let the next prompt retry from scratch
            Kill();
            throw;
        }
    }

    private Task StartAsync()
    {
        if (!TryResolveCommand(out string path))
            throw new FileNotFoundException(Parser.NotFoundMessage);

        ProcessStartInfo psi = new()
        {
            FileName = path,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Cwd,
        };
        Parser.ConfigureArguments(psi, McpUrl, AgentSessionIdText, ResolveMcpServers(), HasEverStarted);

        Process proc = new() { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                RhinoApp.WriteLine($"[{Parser.DisplayName}:err] {e.Data}");
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
        return Task.CompletedTask;
    }

    // The CLI binary search paths are an AgentDefinition concern, but the resolver lives here because
    // it is process spawning, not parsing.
    private bool TryResolveCommand(out string path)
    {
        foreach (string candidate in Definition.SearchPaths)
            if (File.Exists(candidate))
            {
                path = candidate; // first match wins: SearchPaths leads with PATH, the authoritative source
                return true;
            }
        path = string.Empty;
        return false;
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
                    foreach (SessionUpdate update in parsed.Updates ?? [])
                        Push(update);
                    if (parsed.IsTurnComplete)
                        CompleteTurn(token, parsed.Reason, parsed.Usage);
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"[{Parser.DisplayName}] parse error: {ex.Message}");
                }
            }
        }
        finally
        {
            // Read-loop-exit ALWAYS faults the in-flight turn: a CLI that exits without emitting its
            // terminal event must never hang PromptAsync forever. A clean SessionCancelAsync has
            // already won the TCS by here, so this TrySetException is then a no-op.
            TaskCompletionSource<StopReason>? turn = null;
            bool resumeRejected = false;
            lock (Gate)
            {
                // ReferenceEquals(null, null) is true, so the loopback seam (Proc and token both
                // null) takes this branch too; clearing the already-null Proc/Stdin is a no-op there.
                if (ReferenceEquals(Proc, token))
                {
                    turn = CurrentTurn;
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
                    if (ResumePending)
                    {
                        resumeRejected = true;
                        ResumePending = false;
                        HasEverStarted = false;
                        AgentSessionId = Guid.NewGuid();
                    }
                }
            }
            if (resumeRejected)
                Conversation.NoteSystem("could not resume the saved session (it may have expired); started fresh");
            turn?.TrySetException(new IOException($"{Parser.DisplayName} process exited."));
        }
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
