using System.Text.Json;
using Acp;

namespace Acp.Tests;

/// <summary>A minimal agent: answers initialize/new, and on prompt streams one update then ends.</summary>
internal sealed class FakeAgent : IAcpAgent
{
    public IAcpClient? Client { get; set; }
    public bool Cancelled { get; private set; }

    public ValueTask<InitializeResponse> InitializeAsync(InitializeRequest request, CancellationToken ct = default) =>
        ValueTask.FromResult(new InitializeResponse { ProtocolVersion = request.ProtocolVersion });

    public ValueTask<NewSessionResponse> SessionNewAsync(NewSessionRequest request, CancellationToken ct = default) =>
        ValueTask.FromResult(new NewSessionResponse { SessionId = "s1" });

    public async ValueTask<PromptResponse> SessionPromptAsync(PromptRequest request, CancellationToken ct = default)
    {
        await Client!.SessionUpdateAsync(new SessionNotification
        {
            SessionId = request.SessionId,
            Update = new AgentMessageChunkSessionUpdate { Content = new TextContentBlock { Text = "working..." } },
        }, ct);
        return new PromptResponse { StopReason = StopReason.EndTurn };
    }

    public ValueTask SessionCancelAsync(CancelNotification notification, CancellationToken ct = default)
    {
        Cancelled = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask<AuthenticateResponse> AuthenticateAsync(AuthenticateRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<LoadSessionResponse> SessionLoadAsync(LoadSessionRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<SetSessionModeResponse> SessionSetModeAsync(SetSessionModeRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<JsonElement> ExtMethodAsync(string method, JsonElement @params, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask ExtNotificationAsync(string method, JsonElement @params, CancellationToken ct = default) =>
        ValueTask.CompletedTask;
}

/// <summary>A client that records the session/update notifications it receives.</summary>
internal sealed class CapturingClient : IAcpClient
{
    public List<SessionNotification> Updates { get; } = [];

    public ValueTask SessionUpdateAsync(SessionNotification notification, CancellationToken ct = default)
    {
        Updates.Add(notification);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ReadTextFileResponse> FsReadTextFileAsync(ReadTextFileRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<WriteTextFileResponse> FsWriteTextFileAsync(WriteTextFileRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<RequestPermissionResponse> SessionRequestPermissionAsync(RequestPermissionRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<CreateTerminalResponse> TerminalCreateAsync(CreateTerminalRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<KillTerminalCommandResponse> TerminalKillAsync(KillTerminalCommandRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<TerminalOutputResponse> TerminalOutputAsync(TerminalOutputRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<ReleaseTerminalResponse> TerminalReleaseAsync(ReleaseTerminalRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<WaitForTerminalExitResponse> TerminalWaitForExitAsync(WaitForTerminalExitRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<JsonElement> ExtMethodAsync(string method, JsonElement @params, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask ExtNotificationAsync(string method, JsonElement @params, CancellationToken ct = default) =>
        ValueTask.CompletedTask;
}
