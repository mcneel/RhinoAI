using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Acp;

/// <summary>
/// The client end of an ACP connection (the editor/host side). Exposes the agent's methods as
/// outbound calls (<see cref="IAcpAgent"/>) and routes inbound client requests/notifications to the
/// supplied <see cref="IAcpClient"/>.
/// </summary>
public sealed class ClientSideConnection : AcpConnection, IAcpAgent
{
    public ClientSideConnection(Func<IAcpAgent, IAcpClient> toClient, IAcpTransport transport) : base(transport)
    {
        IAcpClient client = toClient(this);

        HandleRequest<ReadTextFileRequest, ReadTextFileResponse>(ClientMethods.FsReadTextFile, client.FsReadTextFileAsync);
        HandleRequest<WriteTextFileRequest, WriteTextFileResponse>(ClientMethods.FsWriteTextFile, client.FsWriteTextFileAsync);
        HandleRequest<RequestPermissionRequest, RequestPermissionResponse>(ClientMethods.SessionRequestPermission, client.SessionRequestPermissionAsync);
        HandleRequest<CreateTerminalRequest, CreateTerminalResponse>(ClientMethods.TerminalCreate, client.TerminalCreateAsync);
        HandleRequest<KillTerminalCommandRequest, KillTerminalCommandResponse>(ClientMethods.TerminalKill, client.TerminalKillAsync);
        HandleRequest<TerminalOutputRequest, TerminalOutputResponse>(ClientMethods.TerminalOutput, client.TerminalOutputAsync);
        HandleRequest<ReleaseTerminalRequest, ReleaseTerminalResponse>(ClientMethods.TerminalRelease, client.TerminalReleaseAsync);
        HandleRequest<WaitForTerminalExitRequest, WaitForTerminalExitResponse>(ClientMethods.TerminalWaitForExit, client.TerminalWaitForExitAsync);
        HandleNotification<SessionNotification>(ClientMethods.SessionUpdate, client.SessionUpdateAsync);

        Endpoint.OnUnknownRequest(async (request, ct) =>
        {
            JsonElement result = await client.ExtMethodAsync(request.Method, request.Params ?? default, ct).ConfigureAwait(false);
            return new JsonRpcResponse { Id = request.Id, Result = result };
        });
        Endpoint.OnUnknownNotification((notification, ct) =>
            client.ExtNotificationAsync(notification.Method, notification.Params ?? default, ct));
    }

    public ValueTask<InitializeResponse> InitializeAsync(InitializeRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<InitializeRequest, InitializeResponse>(AgentMethods.Initialize, request, cancellationToken);

    public ValueTask<AuthenticateResponse> AuthenticateAsync(AuthenticateRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<AuthenticateRequest, AuthenticateResponse>(AgentMethods.Authenticate, request, cancellationToken);

    public ValueTask<NewSessionResponse> SessionNewAsync(NewSessionRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<NewSessionRequest, NewSessionResponse>(AgentMethods.SessionNew, request, cancellationToken);

    public ValueTask<LoadSessionResponse> SessionLoadAsync(LoadSessionRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<LoadSessionRequest, LoadSessionResponse>(AgentMethods.SessionLoad, request, cancellationToken);

    public ValueTask<SetSessionModeResponse> SessionSetModeAsync(SetSessionModeRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<SetSessionModeRequest, SetSessionModeResponse>(AgentMethods.SessionSetMode, request, cancellationToken);

    public ValueTask<PromptResponse> SessionPromptAsync(PromptRequest request, CancellationToken cancellationToken = default) =>
        RequestAsync<PromptRequest, PromptResponse>(AgentMethods.SessionPrompt, request, cancellationToken);

    public ValueTask SessionCancelAsync(CancelNotification notification, CancellationToken cancellationToken = default) =>
        NotifyAsync(AgentMethods.SessionCancel, notification, cancellationToken);

    public ValueTask<JsonElement> ExtMethodAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) =>
        ExtRequestAsync(method, @params, cancellationToken);

    public ValueTask ExtNotificationAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) =>
        Endpoint.SendNotificationAsync(method, @params, cancellationToken);
}
