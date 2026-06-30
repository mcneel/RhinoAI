using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Acp;

/// <summary>
/// A JSON-RPC 2.0 peer over an <see cref="IAcpTransport"/>: correlates outbound requests with their
/// responses by id, and dispatches inbound requests/notifications to registered handlers. One
/// endpoint drives one connection; both ACP sides build on it.
/// </summary>
internal sealed class JsonRpcEndpoint
{
    private IAcpTransport Transport { get; }
    private ConcurrentDictionary<RequestId, TaskCompletionSource<JsonRpcResponse>> Pending { get; } = new();
    private ConcurrentDictionary<string, Func<JsonRpcRequest, CancellationToken, ValueTask<JsonRpcResponse>>> RequestHandlers { get; } = new();
    private ConcurrentDictionary<string, Func<JsonRpcNotification, CancellationToken, ValueTask>> NotificationHandlers { get; } = new();

    private Func<JsonRpcRequest, CancellationToken, ValueTask<JsonRpcResponse>>? DefaultRequestHandler { get; set; }
    private Func<JsonRpcNotification, CancellationToken, ValueTask>? DefaultNotificationHandler { get; set; }

    private long nextId;

    public JsonRpcEndpoint(IAcpTransport transport) => Transport = transport;

    public void OnRequest(string method, Func<JsonRpcRequest, CancellationToken, ValueTask<JsonRpcResponse>> handler) =>
        RequestHandlers[method] = handler;

    public void OnNotification(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler) =>
        NotificationHandlers[method] = handler;

    public void OnUnknownRequest(Func<JsonRpcRequest, CancellationToken, ValueTask<JsonRpcResponse>> handler) =>
        DefaultRequestHandler = handler;

    public void OnUnknownNotification(Func<JsonRpcNotification, CancellationToken, ValueTask> handler) =>
        DefaultNotificationHandler = handler;

    /// <summary>Reads and dispatches messages until the transport closes or cancellation fires.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await Transport.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;                       // stream closed

                string trimmed = line.Trim();
                if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}')
                    continue;                                  // skip banners / non-JSON noise

                await DispatchAsync(line, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // normal shutdown
        }
        finally
        {
            FailPending(new AcpException("Connection closed."));
        }
    }

    // Input is raw stdout from an external agent CLI: the line-51 brace guard is first/last-char
    // only, so a malformed-but-brace-wrapped frame can still fail JsonDocument.Parse or a
    // Deserialize. Any such JsonException must stay contained to this one frame: it must never
    // escape into RunAsync, where the finally would FailPending every in-flight request and kill
    // the read loop. For a method+id frame we can reply with InvalidRequest; for everything else
    // (we may not have an id) we skip the frame and keep reading.
    private async ValueTask DispatchAsync(string line, CancellationToken ct)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            bool hasId = root.TryGetProperty("id", out _);
            bool hasMethod = root.TryGetProperty("method", out _);

            if (hasMethod && hasId)
            {
                JsonRpcRequest request;
                try
                {
                    request = root.Deserialize<JsonRpcRequest>(AcpJson.Options)!;
                }
                catch (JsonException ex)
                {
                    // Reply only if the id itself is well-formed (number/string); otherwise we have
                    // nothing to correlate a response to, so skip the frame.
                    if (TryReadId(root, out RequestId id))
                        await SendAsync(Error(id, JsonRpcErrorCode.InvalidRequest, ex.Message), ct).ConfigureAwait(false);
                    return;
                }
                await HandleRequestAsync(request, ct).ConfigureAwait(false);
            }
            else if (hasMethod)
            {
                JsonRpcNotification notification;
                try
                {
                    notification = root.Deserialize<JsonRpcNotification>(AcpJson.Options)!;
                }
                catch (JsonException)
                {
                    return;
                }
                Func<JsonRpcNotification, CancellationToken, ValueTask>? handler =
                    NotificationHandlers.TryGetValue(notification.Method, out Func<JsonRpcNotification, CancellationToken, ValueTask>? h) ? h : DefaultNotificationHandler;
                if (handler is not null)
                    await handler(notification, ct).ConfigureAwait(false);
            }
            else if (hasId)
            {
                JsonRpcResponse response;
                try
                {
                    response = root.Deserialize<JsonRpcResponse>(AcpJson.Options)!;
                }
                catch (JsonException)
                {
                    return;
                }
                if (Pending.TryRemove(response.Id, out TaskCompletionSource<JsonRpcResponse>? tcs))
                    tcs.TrySetResult(response);
            }
        }
    }

    private async ValueTask HandleRequestAsync(JsonRpcRequest request, CancellationToken ct)
    {
        JsonRpcResponse response;
        try
        {
            Func<JsonRpcRequest, CancellationToken, ValueTask<JsonRpcResponse>>? handler =
                RequestHandlers.TryGetValue(request.Method, out Func<JsonRpcRequest, CancellationToken, ValueTask<JsonRpcResponse>>? h) ? h : DefaultRequestHandler;
            response = handler is not null
                ? await handler(request, ct).ConfigureAwait(false)
                : Error(request.Id, JsonRpcErrorCode.MethodNotFound, $"Method '{request.Method}' is not available");
        }
        catch (AcpException ex)
        {
            response = new JsonRpcResponse { Id = request.Id, Error = new JsonRpcError { Code = ex.Code, Message = ex.Message, Data = ex.ErrorData } };
        }
        catch (Exception ex)
        {
            response = Error(request.Id, JsonRpcErrorCode.InternalError, ex.Message);
        }

        await SendAsync(response, ct).ConfigureAwait(false);
    }

    public async ValueTask<JsonRpcResponse> SendRequestAsync(string method, JsonElement? @params, CancellationToken ct)
    {
        RequestId id = RequestId.Of(Interlocked.Increment(ref nextId));
        TaskCompletionSource<JsonRpcResponse> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[id] = tcs;

        using (ct.Register(static state => ((TaskCompletionSource<JsonRpcResponse>)state!).TrySetCanceled(), tcs))
        {
            await SendAsync(new JsonRpcRequest { Id = id, Method = method, Params = @params }, ct).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    public ValueTask SendNotificationAsync(string method, JsonElement? @params, CancellationToken ct) =>
        SendAsync(new JsonRpcNotification { Method = method, Params = @params }, ct);

    private ValueTask SendAsync<T>(T message, CancellationToken ct) =>
        Transport.WriteLineAsync(JsonSerializer.Serialize(message, AcpJson.Options), ct);

    private static JsonRpcResponse Error(RequestId id, JsonRpcErrorCode code, string message) =>
        new() { Id = id, Error = new JsonRpcError { Code = (int)code, Message = message } };

    private static bool TryReadId(JsonElement root, out RequestId id)
    {
        try
        {
            id = root.GetProperty("id").Deserialize<RequestId>(AcpJson.Options);
            return id.IsValid;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException)
        {
            id = default;
            return false;
        }
    }

    private void FailPending(Exception ex)
    {
        foreach (RequestId key in Pending.Keys)
            if (Pending.TryRemove(key, out TaskCompletionSource<JsonRpcResponse>? tcs))
                tcs.TrySetException(ex);
    }
}
