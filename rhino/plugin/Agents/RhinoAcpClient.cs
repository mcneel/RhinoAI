using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Acp;

namespace RhMcp;

// The client half of an ACP connection for an in-Rhino agent: turns the agent's session/update
// stream into Conversation events, auto-grants tool permission (the only tools are the trusted
// rhino MCP server), and denies filesystem/terminal access (the agent operates only on this doc).
internal sealed class RhinoAcpClient : IAcpClient
{
    private Conversation Conversation { get; }

    public RhinoAcpClient(Conversation conversation) => Conversation = conversation;

    public ValueTask SessionUpdateAsync(SessionNotification notification, CancellationToken cancellationToken = default)
    {
        switch (notification.Update)
        {
            case AgentMessageChunkSessionUpdate chunk:
                Conversation.Record(TurnEventKind.AssistantText, AcpMessageMapper.TextOf(chunk.Content));
                break;
            case ToolCallSessionUpdate call:
                Conversation.Record(TurnEventKind.ToolUse, call.Title, args: Raw(call.RawInput), id: call.ToolCallId);
                break;
            case ToolCallUpdateSessionUpdate update when update.Status == ToolCallStatus.Completed && update.RawOutput is not null:
                Conversation.CompleteToolCall(update.ToolCallId, Raw(update.RawOutput));
                break;
        }
        return default;
    }

    // We trust the rhino MCP tools, so pick an allow option (fallback: the first option) and select
    // it. With no options at all the turn was cancelled.
    public ValueTask<RequestPermissionResponse> SessionRequestPermissionAsync(RequestPermissionRequest request, CancellationToken cancellationToken = default)
    {
        PermissionOption? allow = request.Options.FirstOrDefault(o => o.Kind is PermissionOptionKind.AllowOnce or PermissionOptionKind.AllowAlways)
            ?? request.Options.FirstOrDefault();

        RequestPermissionOutcome outcome = allow is null
            ? new CancelledRequestPermissionOutcome()
            : new SelectedRequestPermissionOutcome { OptionId = allow.OptionId };

        return new ValueTask<RequestPermissionResponse>(new RequestPermissionResponse { Outcome = outcome });
    }

    public ValueTask<ReadTextFileResponse> FsReadTextFileAsync(ReadTextFileRequest request, CancellationToken cancellationToken = default) =>
        throw Denied("filesystem read");

    public ValueTask<WriteTextFileResponse> FsWriteTextFileAsync(WriteTextFileRequest request, CancellationToken cancellationToken = default) =>
        throw Denied("filesystem write");

    public ValueTask<CreateTerminalResponse> TerminalCreateAsync(CreateTerminalRequest request, CancellationToken cancellationToken = default) =>
        throw Denied("terminal");

    public ValueTask<KillTerminalCommandResponse> TerminalKillAsync(KillTerminalCommandRequest request, CancellationToken cancellationToken = default) =>
        throw Denied("terminal");

    public ValueTask<TerminalOutputResponse> TerminalOutputAsync(TerminalOutputRequest request, CancellationToken cancellationToken = default) =>
        throw Denied("terminal");

    public ValueTask<ReleaseTerminalResponse> TerminalReleaseAsync(ReleaseTerminalRequest request, CancellationToken cancellationToken = default) =>
        throw Denied("terminal");

    public ValueTask<WaitForTerminalExitResponse> TerminalWaitForExitAsync(WaitForTerminalExitRequest request, CancellationToken cancellationToken = default) =>
        throw Denied("terminal");

    public ValueTask<JsonElement> ExtMethodAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) =>
        throw Denied(method);

    public ValueTask ExtNotificationAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) => default;

    private static string Raw(JsonElement? value) => value is { } v ? v.GetRawText() : string.Empty;

    private static AcpException Denied(string what) =>
        new($"{what} is not available to in-Rhino agents", (int)Acp.JsonRpcErrorCode.MethodNotFound);
}
