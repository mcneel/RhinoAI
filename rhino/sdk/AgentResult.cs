using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

/// <summary>
/// The result of an Agentic request
/// </summary>
public interface IAgentResult : IReadOnlyList<ITurn>
{

    /// <summary>
    /// The Turns from the result.
    /// </summary>
    /// <remarks></remarks>
    public IReadOnlyList<ITurn> Turns { get; }

    /// <summary>
    /// The total number of tokens used
    /// </summary>
    public int TokenCount { get; }

    /// <summary>
    /// Reason for the request stopping
    /// </summary>
    public StopReason Reason { get; }
    
    /// <summary>
    /// true if the result was a success.
    /// </summary>
    /// <remarks>This could still result in failure</remarks>
    public bool Success { get; }

    public string? Error { get; }
    
    /// <summary>
    /// The total duration of the request
    /// </summary>
    public TimeSpan Duration { get; }

    public string? Text { get; }

    /// <summary>
    /// Any tool calls made during the request
    /// </summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; }

}

public sealed class StreamedAgentResult : IAgentResult, IAsyncEnumerable<ITurn>
{

    private IAsyncEnumerable<ITurn> Source { get; }

    private List<ITurn> Received { get; } = [];

    private enum StreamState { NotStarted, Streaming, Complete, Aborted }

    private StreamState State { get; set; } = StreamState.NotStarted;

    internal StreamedAgentResult(IAsyncEnumerable<ITurn> source)
    {
        Source = source;
    }

    public IReadOnlyList<ITurn> Turns => Received;

    public int TokenCount => AgentResult.TokenCountOf(Received);

    public StopReason Reason => AgentResult.ReasonOf(Received);

    public bool Success => AgentResult.SuccessOf(Received);

    public string? Error => AgentResult.ErrorOf(Received);

    public TimeSpan Duration => AgentResult.DurationOf(Received);

    public string? Text => AgentResult.TextOf(Received);

    public IReadOnlyList<ToolCall> ToolCalls => AgentResult.ToolCallsOf(Received);

    public int Count => Received.Count;

    public ITurn this[int index] => Received[index];

    public IEnumerator<ITurn> GetEnumerator() => Received.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public async IAsyncEnumerator<ITurn> GetAsyncEnumerator(CancellationToken token = default)
    {
        // Each enumeration of the source sends the message to the model again.
        switch (State)
        {
            case StreamState.Complete:
                foreach (ITurn turn in Received)
                    yield return turn;
                yield break;

            case StreamState.Streaming:
                throw new InvalidOperationException($"This {nameof(StreamedAgentResult)} is still streaming.");

            case StreamState.Aborted:
                throw new InvalidOperationException($"This {nameof(StreamedAgentResult)} stopped before it finished, so it cannot be replayed.");

            case StreamState.NotStarted:
                break;
        }

        State = StreamState.Streaming;

        try
        {
            await foreach (ITurn turn in Source.WithCancellation(token).ConfigureAwait(false))
            {
                Received.Add(turn);
                yield return turn;
            }

            State = StreamState.Complete;
        }
        finally
        {
            if (State == StreamState.Streaming)
                State = StreamState.Aborted;
        }
    }

}

internal sealed class AgentResult : IAgentResult
{

    /// <summary>
    /// All the turns returned by the Request
    /// </summary>
    public IReadOnlyList<ITurn> Turns { get; }

    /// <summary>
    /// The total token count of al lthe turns
    /// </summary>
    public int TokenCount { get; }

    public StopReason Reason { get; }

    public bool Success { get; }

    public string? Error { get; }

    public TimeSpan Duration { get; }

    public string? Text { get; }

    /// <summary>
    /// All of the calls made to tools in this request
    /// </summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; }

    internal AgentResult(IReadOnlyList<ITurn> turns)
    {
        Turns = turns;
        TokenCount = TokenCountOf(turns);
        Reason = ReasonOf(turns);
        Success = SuccessOf(turns);
        Error = ErrorOf(turns);
        Duration = DurationOf(turns);
        Text = TextOf(turns);
        ToolCalls = ToolCallsOf(turns);
    }

    private static TurnEnd? EndOf(IReadOnlyList<ITurn> turns) => turns.OfType<TurnEnd>().LastOrDefault();

    internal static int TokenCountOf(IReadOnlyList<ITurn> turns) => turns.Sum(t => t.TokenCount ?? 0);

    internal static StopReason ReasonOf(IReadOnlyList<ITurn> turns) => EndOf(turns)?.Reason ?? StopReason.Other;

    internal static bool SuccessOf(IReadOnlyList<ITurn> turns) => EndOf(turns)?.Success ?? false;

    internal static string? ErrorOf(IReadOnlyList<ITurn> turns)
        => EndOf(turns) is { Data.Length: > 0 } end ? end.Data : null;

    internal static TimeSpan DurationOf(IReadOnlyList<ITurn> turns)
        => turns.Count == 0 ? TimeSpan.Zero : turns[^1].TimeStamp - turns[0].TimeStamp;

    internal static string? TextOf(IReadOnlyList<ITurn> turns)
        => turns.OfType<MessageTurn>().LastOrDefault(m => m.Role == RoleType.Assistant)?.Data;

    internal static IReadOnlyList<ToolCall> ToolCallsOf(IReadOnlyList<ITurn> turns)
    {
        Dictionary<string, ToolResultTurn> results = turns.OfType<ToolResultTurn>()
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.Last());

        return [.. turns.OfType<ToolTurn>().Select(t => new ToolCall(t, results.GetValueOrDefault(t.Id)))];
    }

    public int Count => Turns.Count;

    public ITurn this[int index] => Turns[index];

    public IEnumerator<ITurn> GetEnumerator() => Turns.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

}
