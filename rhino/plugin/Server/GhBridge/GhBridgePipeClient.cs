using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server.GhBridge;

// BRIDGE SEAM (added, not upstream). Vendored duplicate of the framing in
// src/GrasshopperAITools.Protocol/Transport/PipeFraming.cs — see GhBridgeProtocol.cs
// for why it is copied rather than referenced.

/// <summary>
/// Minimal named-pipe client for the GrasshopperAITools bridge: connect with a timeout,
/// write one length-prefixed UTF-8 JSON frame, read the framed reply, correlate it by id.
/// </summary>
/// <remarks>
/// <para>
/// The server accepts <b>one client at a time</b>, so a connection is held for exactly one
/// exchange and then dropped. Keeping a session open between tool calls would starve every
/// other client of the pipe for the lifetime of Rhino.
/// </para>
/// <para>
/// Every call runs on a background thread (the gateway tools are <c>[BackgroundThread]</c>).
/// It must never run on the Rhino UI thread: the server's execute path marshals the
/// Grasshopper solution onto that same UI thread, so a UI-thread pipe read here deadlocks
/// both processes' shared message loop on every single call.
/// </para>
/// </remarks>
internal sealed class GhBridgePipeClient : IDisposable
{
    /// <summary>
    /// How many frames to read while looking for the one matching the request id. A stale
    /// frame left over from a previous, timed-out exchange is the only realistic reason to
    /// see a mismatch; this bounds the search so a chatty server cannot hang the call.
    /// </summary>
    private const int MaxFramesPerResponse = 8;

    private readonly NamedPipeClientStream _pipe;
    private int _nextId;
    private bool _disposed;

    private GhBridgePipeClient(NamedPipeClientStream pipe) => _pipe = pipe;

    /// <summary>
    /// Connects to the bridge, or throws <see cref="TimeoutException"/> if nothing accepts
    /// within <paramref name="connectTimeoutMs"/>.
    /// </summary>
    public static async Task<GhBridgePipeClient> ConnectAsync(
        int connectTimeoutMs, CancellationToken ct = default)
    {
        NamedPipeClientStream pipe = new(
            ".", GhBridgeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(connectTimeoutMs, ct).ConfigureAwait(false);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
        return new GhBridgePipeClient(pipe);
    }

    /// <summary>
    /// Issues one request and returns its <c>result</c> payload.
    /// </summary>
    /// <exception cref="TimeoutException">The server did not answer in time.</exception>
    /// <exception cref="IOException">The pipe broke or the server closed it mid-exchange.</exception>
    /// <exception cref="GhBridgeException">The server answered with an <c>error</c> envelope.</exception>
    public async Task<JsonElement> InvokeAsync(
        string method, JsonObject? parameters, int timeoutMs, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string id = Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
        JsonObject request = new() { ["id"] = id, ["method"] = method };
        if (parameters is not null)
            request["params"] = parameters;

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            await WriteFrameAsync(request.ToJsonString(), cts.Token).ConfigureAwait(false);

            for (int attempt = 0; attempt < MaxFramesPerResponse; attempt++)
            {
                string? frame = await ReadFrameAsync(cts.Token).ConfigureAwait(false);
                if (frame is null)
                    throw new IOException(
                        $"The Grasshopper bridge closed the pipe without answering '{method}'.");

                GhBridgeResponse? response;
                try
                {
                    response = JsonSerializer.Deserialize<GhBridgeResponse>(
                        frame, GhBridgeProtocol.SerializerOptions);
                }
                catch (JsonException ex)
                {
                    throw new IOException(
                        $"The Grasshopper bridge sent a frame that is not a valid response: {ex.Message}", ex);
                }

                if (response is null)
                    throw new IOException("The Grasshopper bridge sent an empty response.");

                // Id correlation: skip anything that is not this request's answer rather
                // than mistaking a stale frame for it.
                if (!string.Equals(response.Id, id, StringComparison.Ordinal))
                    continue;

                if (response.Error is not null)
                    throw new GhBridgeException(
                        response.Error.Message ?? $"The Grasshopper bridge rejected '{method}'.",
                        response.Error.Code);

                return response.Result ?? default;
            }

            throw new IOException(
                $"The Grasshopper bridge sent no frame carrying id '{id}' for '{method}'.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The Grasshopper bridge did not answer '{method}' within {timeoutMs} ms.");
        }
    }

    /// <summary>4-byte big-endian length prefix, then the UTF-8 JSON payload.</summary>
    private async Task WriteFrameAsync(string json, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] header = new byte[GhBridgeProtocol.FrameHeaderBytes];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);

        await _pipe.WriteAsync(header, 0, header.Length, ct).ConfigureAwait(false);
        if (payload.Length > 0)
            await _pipe.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
        await _pipe.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads one frame; null means the peer closed the pipe cleanly.</summary>
    private async Task<string?> ReadFrameAsync(CancellationToken ct)
    {
        byte[]? header = await ReadExactlyAsync(GhBridgeProtocol.FrameHeaderBytes, ct).ConfigureAwait(false);
        if (header is null)
            return null;

        uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length == 0)
            return string.Empty;

        byte[]? payload = await ReadExactlyAsync((int)length, ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        return Encoding.UTF8.GetString(payload);
    }

    private async Task<byte[]?> ReadExactlyAsync(int count, CancellationToken ct)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await _pipe.ReadAsync(buffer, offset, count - offset, ct).ConfigureAwait(false);
            if (read == 0)
                return null;
            offset += read;
        }
        return buffer;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        { _pipe.Dispose(); }
        catch { }
    }
}
