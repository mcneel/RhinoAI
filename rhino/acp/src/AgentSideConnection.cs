using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Acp;

/// <summary>
/// The agent end of an ACP connection. Exposes the client's methods as outbound calls
/// (<see cref="IAcpClient"/>) and routes inbound agent requests/notifications to the supplied
/// <see cref="IAcpAgent"/>. Used to wrap an in-process or subprocess agent, and for the loopback test.
/// </summary>
public sealed class AgentSideConnection : AcpConnection, IAcpClient
{
    public AgentSideConnection(IAcpAgent agent, IAcpTransport transport) : base(transport)
    {
        HandleRequest<InitializeRequest, InitializeResponse>(AgentMethods.Initialize, agent.InitializeAsync);
        HandleRequest<AuthenticateRequest, AuthenticateResponse>(AgentMethods.Authenticate, agent.AuthenticateAsync);
        HandleRequest<NewSessionRequest, NewSessionResponse>(AgentMethods.SessionNew, agent.SessionNewAsync);
        HandleRequest<LoadSessionRequest, LoadSessionResponse>(AgentMethods.SessionLoad, agent.SessionLoadAsync);
        HandleRequest<SetSessionModeRequest, SetSessionModeResponse>(AgentMethods.SessionSetMode, agent.SessionSetModeAsync);
        HandleRequest<PromptRequest, PromptResponse>(AgentMethods.SessionPrompt, agent.SessionPromptAsync);
        HandleNotification<CancelNotification>(AgentMethods.SessionCancel, agent.SessionCancelAsync);

        Endpoint.OnUnknownRequest(async (request, ct) =>
        {
            JsonElement result = await agent.ExtMethodAsync(request.Method, request.Params ?? default, ct).ConfigureAwait(false);
            return new JsonRpcResponse { Id = request.Id, Result = result };
        });
        Endpoint.OnUnknownNotification((notification, ct) =>
            agent.ExtNotificationAsync(notification.Method, notification.Params ?? default, ct));
    }

    public ValueTask<ReadTextFileResponse> FsReadTextFileAsync(ReadTextFileRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<ReadTextFileRequest, ReadTextFileResponse>(ClientMethods.FsReadTextFile, request, cancellationToken);

    public ValueTask<WriteTextFileResponse> FsWriteTextFileAsync(WriteTextFileRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<WriteTextFileRequest, WriteTextFileResponse>(ClientMethods.FsWriteTextFile, request, cancellationToken);

    public ValueTask<RequestPermissionResponse> SessionRequestPermissionAsync(RequestPermissionRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<RequestPermissionRequest, RequestPermissionResponse>(ClientMethods.SessionRequestPermission, request, cancellationToken);

    public ValueTask<CreateTerminalResponse> TerminalCreateAsync(CreateTerminalRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<CreateTerminalRequest, CreateTerminalResponse>(ClientMethods.TerminalCreate, request, cancellationToken);

    public ValueTask<KillTerminalCommandResponse> TerminalKillAsync(KillTerminalCommandRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<KillTerminalCommandRequest, KillTerminalCommandResponse>(ClientMethods.TerminalKill, request, cancellationToken);

    public ValueTask<TerminalOutputResponse> TerminalOutputAsync(TerminalOutputRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<TerminalOutputRequest, TerminalOutputResponse>(ClientMethods.TerminalOutput, request, cancellationToken);

    public ValueTask<ReleaseTerminalResponse> TerminalReleaseAsync(ReleaseTerminalRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<ReleaseTerminalRequest, ReleaseTerminalResponse>(ClientMethods.TerminalRelease, request, cancellationToken);

    public ValueTask<WaitForTerminalExitResponse> TerminalWaitForExitAsync(WaitForTerminalExitRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<WaitForTerminalExitRequest, WaitForTerminalExitResponse>(ClientMethods.TerminalWaitForExit, request, cancellationToken);

    public ValueTask SessionUpdateAsync(SessionNotification notification, CancellationToken cancellationToken = default) =>
        NotifyAsync(ClientMethods.SessionUpdate, notification, cancellationToken);

    public ValueTask<JsonElement> ExtMethodAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) =>
        ExtRequestAsync(method, @params, cancellationToken);

    public ValueTask ExtNotificationAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) =>
        Endpoint.SendNotificationAsync(method, @params, cancellationToken);
}
