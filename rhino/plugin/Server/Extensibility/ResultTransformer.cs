using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server.Extensibility;

/// <summary>
/// One result transformer, as registered by a contributing Rhino plug-in through
/// <see cref="McpExtensionHost.RegisterMcpResultTransform"/>.
/// </summary>
/// <remarks>
/// A transformer may rewrite the result of <em>any</em> tool call this server serves,
/// including tools compiled into this plug-in. It is a decorator and nothing more: it
/// cannot veto a call, cannot turn a result into a protocol error, and cannot fail a
/// call that succeeded -- one that throws, hangs or returns unusable JSON is skipped
/// and the previous result is kept.
/// </remarks>
internal sealed class ResultTransformer
{
    /// <summary>
    /// The reverse-DNS identifier of the plug-in that registered this transformer, e.g.
    /// <c>com.example.myplugin</c>.
    /// </summary>
    /// <remarks>
    /// Doubles as the registration key -- re-registering under the same owner replaces the
    /// previous transformer rather than adding a second -- and as the tie-breaker that makes
    /// <see cref="Order"/> deterministic.
    /// </remarks>
    public string Owner { get; }

    /// <summary>
    /// Position in the chain. Transformers run in ascending order, each receiving the
    /// previous one's output; ties are broken by <see cref="Owner"/>.
    /// </summary>
    /// <remarks>
    /// Ordering is broken by owner rather than by registration order on purpose: registration
    /// order depends on which plug-ins happened to load first, so it can differ between
    /// sessions, and a chain that reorders itself between runs is not debuggable.
    /// </remarks>
    public int Order { get; }

    /// <summary>
    /// The transformer. Takes a context JSON object
    /// (<c>{ "tool", "arguments", "endpoint", "source", "owner" }</c>) and the result JSON,
    /// and returns a replacement result -- or null, empty, or its input unchanged to decline.
    /// </summary>
    /// <remarks>
    /// Every parameter and the return value are corelib types, which is what lets this cross
    /// an <c>AssemblyLoadContext</c> boundary safely: see the remarks on
    /// <see cref="McpExtensionHost"/> for why anything from <c>System.Text.Json</c> could not.
    /// </remarks>
    public Func<string, string, CancellationToken, Task<string>> Transform { get; }

    /// <summary>
    /// Creates a transformer record. Validation of <paramref name="owner"/> and of the
    /// delegate happens at the registration boundary, not here.
    /// </summary>
    /// <param name="owner">The registering plug-in's identifier. See <see cref="Owner"/>.</param>
    /// <param name="order">Position in the chain. See <see cref="Order"/>.</param>
    /// <param name="transform">The transformer itself. See <see cref="Transform"/>.</param>
    public ResultTransformer(string owner, int order, Func<string, string, CancellationToken, Task<string>> transform)
    {
        Owner = owner;
        Order = order;
        Transform = transform;
    }
}
