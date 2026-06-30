using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Acp;

/// <summary>
/// A transport over a process's stdio (or any reader/writer pair). Reads newline-delimited JSON
/// from the reader and writes it to the writer, which is forced to <c>\n</c> line endings and
/// flushed per message so the peer sees each line immediately.
/// </summary>
public sealed class StdioTransport : IAcpTransport
{
    private TextReader Reader { get; }
    private TextWriter Writer { get; }
    private SemaphoreSlim WriteGate { get; } = new(1, 1);

    public StdioTransport(TextReader reader, TextWriter writer)
    {
        Reader = reader;
        Writer = writer;
        Writer.NewLine = "\n";
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
        await Reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask WriteLineAsync(string json, CancellationToken cancellationToken = default)
    {
        await WriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    public void Dispose() => WriteGate.Dispose();
}
