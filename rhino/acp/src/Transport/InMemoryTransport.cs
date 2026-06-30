using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Acp;

/// <summary>
/// An in-process transport pair joined by two channels. <see cref="CreatePair"/> returns the two
/// ends of one connection, used to wire a <c>ClientSideConnection</c> directly to an
/// <c>AgentSideConnection</c> with no subprocess (tests, in-proc agents).
/// </summary>
public sealed class InMemoryTransport : IAcpTransport
{
    private ChannelReader<string> Incoming { get; }
    private ChannelWriter<string> Outgoing { get; }

    private InMemoryTransport(ChannelReader<string> incoming, ChannelWriter<string> outgoing)
    {
        Incoming = incoming;
        Outgoing = outgoing;
    }

    /// <summary>Creates the two connected ends of a single in-memory link.</summary>
    public static (IAcpTransport A, IAcpTransport B) CreatePair()
    {
        Channel<string> aToB = Channel.CreateUnbounded<string>();
        Channel<string> bToA = Channel.CreateUnbounded<string>();
        InMemoryTransport a = new(bToA.Reader, aToB.Writer);
        InMemoryTransport b = new(aToB.Reader, bToA.Writer);
        return (a, b);
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Incoming.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public ValueTask WriteLineAsync(string json, CancellationToken cancellationToken = default) =>
        Outgoing.WriteAsync(json, cancellationToken);

    public void Dispose() => Outgoing.TryComplete();
}
