using System;
using System.Threading;
using System.Threading.Tasks;

namespace Acp;

/// <summary>
/// A bidirectional, newline-delimited JSON message channel. One line per JSON-RPC message.
/// Implementations carry raw text only; framing and (de)serialization live above this seam.
/// </summary>
public interface IAcpTransport : IDisposable
{
    /// <summary>Reads the next line, or null at end of stream.</summary>
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes one line (a single JSON message) and flushes it.</summary>
    ValueTask WriteLineAsync(string json, CancellationToken cancellationToken = default);
}
