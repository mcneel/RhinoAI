using System.Runtime.CompilerServices;

using Rhino.AI;
using Rhino.AI.Models;
using Rhino.AI.PlugIns;

namespace sdk.tests;

public class LoopTests
{

    private sealed class ScriptedModel(params Func<IReadOnlyList<ITurn>>[] replies) : IModel
    {
        public string Name => "scripted";

        public string Vendor => "test";

        private int Requests { get; set; }

        public async IAsyncEnumerable<ITurn> StreamAsync(Harness harness, IEnumerable<ITurn> turns, [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            foreach (ITurn turn in replies[Requests++]())
                yield return turn;
        }

        public Task<IEnumerable<ITurn>> SendAsync(Harness harness, IEnumerable<ITurn> turns, CancellationToken token)
            => throw new NotSupportedException();
    }

    private static IReadOnlyList<ITurn> CallsATool() =>
    [
        new ToolTurn("call-1", "unparseable", []),
        new UsageTurn(100, 10),
        new TurnEnd(StopReason.ToolUse),
    ];

    private static Agent AgentWith(IModel model)
    {
        TestDefinition definition = new();
        definition.Add(model);
        AIInstances.Agents.Add(definition);

        return definition.GetAgent(PlugInToken.Invalid) ?? throw new InvalidOperationException("No agent.");
    }

    [Test, CancelAfter(5000)]
    public async Task EveryRequestReportsItsOwnUsage(CancellationToken token)
    {
        Agent agent = AgentWith(new ScriptedModel(
            CallsATool,
            () => [new MessageTurn("done", RoleType.Assistant), new UsageTurn(150, 5), new TurnEnd(StopReason.EndTurn)]));

        IAgentResult result = await agent.SendAsync("go", token);

        Assert.That(result.Turns.OfType<UsageTurn>().Select(u => u.TokenCount), Is.EqualTo(new[] { 110, 155 }));
        Assert.That(result.TokenCount, Is.EqualTo(265));

        TurnEnd end = result.Turns.OfType<TurnEnd>().Single();
        Assert.That(end.Reason, Is.EqualTo(StopReason.EndTurn));
        Assert.That(end.TokenCount, Is.Null);
    }

    [Test, CancelAfter(5000)]
    public void AFailedRequestKeepsTheUsageAlreadyReported(CancellationToken token)
    {
        Agent agent = AgentWith(new ScriptedModel(
            CallsATool,
            () => throw new HttpRequestException("rejected")));

        List<ITurn> received = [];
        Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (ITurn turn in agent.StreamAsync("go", token))
                received.Add(turn);
        });

        Assert.That(received.OfType<UsageTurn>().Single().TokenCount, Is.EqualTo(110));
        Assert.That(received.OfType<TurnEnd>(), Is.Empty);
    }

    [Test, CancelAfter(5000)]
    public async Task RunningOutOfTokensWhileThinkingIsAFailure(CancellationToken token)
    {
        Agent agent = AgentWith(new ScriptedModel(
            () => [new ThinkingTurn("still working it out"), new UsageTurn(100, 8192), new TurnEnd(StopReason.MaxTokens)]));

        IAgentResult result = await agent.SendAsync("go", token);

        Assert.That(result.Reason, Is.EqualTo(StopReason.RanOutOfTokens));
        Assert.That(result.Success, Is.False);
        Assert.That(result.Text, Is.Null);
    }

    [Test, CancelAfter(5000)]
    public async Task AReplyCutOffByTheTokenCapKeepsItsTextButIsAFailure(CancellationToken token)
    {
        Agent agent = AgentWith(new ScriptedModel(
            () => [new ThinkingTurn("planning"), new MessageTurn("Here is the first half", RoleType.Assistant), new UsageTurn(100, 8192), new TurnEnd(StopReason.MaxTokens)]));

        IAgentResult result = await agent.SendAsync("go", token);

        Assert.That(result.Reason, Is.EqualTo(StopReason.MaxTokens));
        Assert.That(result.Success, Is.False);
        Assert.That(result.Text, Is.EqualTo("Here is the first half"));
    }

    [Test, CancelAfter(5000)]
    public async Task OnlyTheLastResponseDecidesWhetherTheTokenCapCutOffAReply(CancellationToken token)
    {
        Agent agent = AgentWith(new ScriptedModel(
            () => [new MessageTurn("Let me look", RoleType.Assistant), .. CallsATool()],
            () => [new ThinkingTurn("still working it out"), new UsageTurn(150, 8192), new TurnEnd(StopReason.MaxTokens)]));

        IAgentResult result = await agent.SendAsync("go", token);

        Assert.That(result.Reason, Is.EqualTo(StopReason.RanOutOfTokens));
        Assert.That(result.Success, Is.False);
    }

    [Test, CancelAfter(5000)]
    public async Task WhitespaceBeforeTheTokenCapIsNotAReply(CancellationToken token)
    {
        Agent agent = AgentWith(new ScriptedModel(
            () => [new MessageTurn("\n\n", RoleType.Assistant), new ThinkingTurn("still working it out"), new UsageTurn(100, 8192), new TurnEnd(StopReason.MaxTokens)]));

        IAgentResult result = await agent.SendAsync("go", token);

        Assert.That(result.Reason, Is.EqualTo(StopReason.RanOutOfTokens));
    }

    [Test, CancelAfter(5000)]
    public async Task ANormalEndIsStillASuccess(CancellationToken token)
    {
        Agent agent = AgentWith(new ScriptedModel(
            () => [new ThinkingTurn("planning"), new MessageTurn("done", RoleType.Assistant), new UsageTurn(100, 5), new TurnEnd(StopReason.EndTurn)]));

        IAgentResult result = await agent.SendAsync("go", token);

        Assert.That(result.Reason, Is.EqualTo(StopReason.EndTurn));
        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Text, Is.EqualTo("done"));
    }

    [Test]
    public void ContextEstimateStartsFromTheLatestReport()
    {
        List<ITurn> history =
        [
            new MessageTurn(new string('a', 4000), RoleType.User),
            new UsageTurn(1000, 50),
            new ToolResultTurn("call-1", "tool", ToolReturn.Success(new string('b', 400))),
            new UsageTurn(1200, 30),
            new MessageTurn(new string('c', 40), RoleType.User),
        ];

        Assert.That(TokenEstimate.FromTurnsPreferringReported(history), Is.EqualTo(1200 + 30 + 10));
    }

}
