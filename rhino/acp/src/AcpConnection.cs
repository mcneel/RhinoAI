using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Acp;

/// <summary>
/// Shared plumbing for the two ACP role connections: owns the <see cref="JsonRpcEndpoint"/>, runs
/// its read loop, and provides typed send/handle helpers that (de)serialize ACP message records.
/// </summary>
public abstract class AcpConnection : IDisposable
{
    private protected JsonRpcEndpoint Endpoint { get; }
    private IAcpTransport Transport { get; }
    private CancellationTokenSource Cts { get; } = new();
    private Task? ReadLoop { get; set; }

    private protected AcpConnection(IAcpTransport transport)
    {
        Transport = transport;
        Endpoint = new JsonRpcEndpoint(transport);
    }

    /// <summary>
    /// The background read loop, faulting only on an unexpected failure. The owner can observe this
    /// to learn the connection has died (a clean stream close or cancellation completes it). Throws
    /// if <see cref="Start"/> has not been called.
    /// </summary>
    public Task Completion => ReadLoop ?? throw new InvalidOperationException("Start has not been called.");

    /// <summary>
    /// Starts the background read loop. Call once after construction; a second call is a no-op so we
    /// never spawn a duplicate loop on the same transport.
    /// </summary>
    public void Start() => ReadLoop ??= Task.Run(() => Endpoint.RunAsync(Cts.Token));

    private protected async ValueTask<TResponse> RequestAsync<TRequest, TResponse>(string method, TRequest request, CancellationToken ct)
    {
        JsonElement @params = JsonSerializer.SerializeToElement(request, AcpJson.Options);
        JsonRpcResponse response = await Endpoint.SendRequestAsync(method, @params, ct).ConfigureAwait(false);
        if (response.Error is not null)
            throw new AcpException(response.Error);
        if (response.Result is not { } result)
            throw new AcpException($"Response to '{method}' had neither result nor error.");
        return result.Deserialize<TResponse>(AcpJson.Options)!;
    }

    private protected ValueTask NotifyAsync<TNotification>(string method, TNotification notification, CancellationToken ct) =>
        Endpoint.SendNotificationAsync(method, JsonSerializer.SerializeToElement(notification, AcpJson.Options), ct);

    private protected async ValueTask<JsonElement> ExtRequestAsync(string method, JsonElement @params, CancellationToken ct)
    {
        JsonRpcResponse response = await Endpoint.SendRequestAsync(method, @params, ct).ConfigureAwait(false);
        if (response.Error is not null)
            throw new AcpException(response.Error);
        return response.Result ?? default;
    }

    private protected void HandleRequest<TRequest, TResponse>(string method, Func<TRequest, CancellationToken, ValueTask<TResponse>> impl) =>
        Endpoint.OnRequest(method, async (request, ct) =>
        {
            if (request.Params is not { } pe)
                throw new AcpException($"Missing params for '{method}'", (int)JsonRpcErrorCode.InvalidParams);
            TRequest parsed = pe.Deserialize<TRequest>(AcpJson.Options)!;
            TResponse result = await impl(parsed, ct).ConfigureAwait(false);
            return new JsonRpcResponse { Id = request.Id, Result = JsonSerializer.SerializeToElement(result, AcpJson.Options) };
        });

    private protected void HandleNotification<TNotification>(string method, Func<TNotification, CancellationToken, ValueTask> impl) =>
        Endpoint.OnNotification(method, async (notification, ct) =>
        {
            if (notification.Params is not { } pe)
                throw new AcpException($"Missing params for notification '{method}'", (int)JsonRpcErrorCode.InvalidParams);
            TNotification parsed = pe.Deserialize<TNotification>(AcpJson.Options)!;
            await impl(parsed, ct).ConfigureAwait(false);
        });

    public void Dispose()
    {
        Cts.Cancel();
        Transport.Dispose();
        Cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
