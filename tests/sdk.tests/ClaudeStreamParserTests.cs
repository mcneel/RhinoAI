using Rhino.AI;
using Rhino.AI.Models;
using Rhino.AI.PlugIns;

namespace sdk.tests;

public class ClaudeStreamParserTests
{

    private static Guid Launched { get; } = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private sealed class Replay
    {
        public Guid? SessionId { get; private set; }

        public ClaudeStreamParser Parser { get; }

        public Replay() => Parser = new(new Harness(PlugInToken.Invalid), Launched, reported => SessionId = reported);

        public List<ITurn> Run(string recording) => Lines(recording).SelectMany(Parser.Parse).ToList();
    }

    private static string ArgsJson(ITurn turn) => ToolArgs.ToJson(((ToolTurn)turn).Args).ToJsonString();

    [Test]
    public void PlainReply()
    {
        Replay replay = new();
        List<ITurn> turns = replay.Run("claude-reply.jsonl");

        Assert.That(replay.SessionId, Is.EqualTo(Guid.Parse("83faae92-ea66-4ce7-ae41-3f32a794f749")));
        Assert.That(turns.Select(t => t.GetType()), Is.EqualTo(new[] { typeof(MessageTurn), typeof(UsageTurn), typeof(TurnEnd) }));

        MessageTurn reply = (MessageTurn)turns[0];
        Assert.That(reply.Role, Is.EqualTo(RoleType.Assistant));
        Assert.That(reply.Message, Is.EqualTo("hello from Rhino"));
        Assert.That(reply.TimeStamp, Is.EqualTo(new DateTime(2026, 11, 27, 18, 7, 49, 134, DateTimeKind.Utc)));

        UsageTurn usage = (UsageTurn)turns[1];
        Assert.That(usage.InputTokens, Is.EqualTo(2 + 4459), "Cache writes count as input.");
        Assert.That(usage.OutputTokens, Is.EqualTo(9));
        Assert.That(usage.TokenCount, Is.EqualTo(4470));

        TurnEnd end = (TurnEnd)turns[2];
        Assert.That(end.Reason, Is.EqualTo(StopReason.EndTurn));
        Assert.That(end.Success, Is.True);
        Assert.That(end.TokenCount, Is.Null);
        Assert.That(end.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(446)));
    }

    [Test]
    public void DeniedAndAllowedToolCalls()
    {
        List<ITurn> turns = new Replay().Run("claude-tools.jsonl");

        Assert.That(turns.Select(t => t.GetType()), Is.EqualTo(new[]
        {
            typeof(ToolTurn), typeof(ToolResultTurn),
            typeof(ToolTurn), typeof(ToolResultTurn),
            typeof(MessageTurn), typeof(UsageTurn), typeof(TurnEnd),
        }), "The thinking block arrives with empty text, so it should produce no turn.");

        ToolTurn ping = (ToolTurn)turns[0];
        Assert.That(ping.Id, Is.EqualTo("toolu_012pE6oNqX3L2dXC3rXXAqCt"));
        Assert.That(ping.Name, Is.EqualTo("mcp__Test__ping"));
        Assert.That(ping.Args, Is.Empty);

        ToolResultTurn denied = (ToolResultTurn)turns[1];
        Assert.That(denied.Id, Is.EqualTo(ping.Id));
        Assert.That(denied.Name, Is.EqualTo("mcp__Test__ping"));
        Assert.That(denied.Return.Result, Is.EqualTo(ToolResult.Failure));
        Assert.That(denied.Return.Message, Is.EqualTo("Claude requested permissions to use mcp__Test__ping, but you haven't granted it yet."));

        Assert.That(((ToolTurn)turns[2]).Name, Is.EqualTo("mcp__Test__repeat"));
        Assert.That(ArgsJson(turns[2]), Is.EqualTo("""{"text":"ab","count":3}"""));

        ToolResultTurn repeated = (ToolResultTurn)turns[3];
        Assert.That(repeated.Name, Is.EqualTo("mcp__Test__repeat"));
        Assert.That(repeated.Return.Result, Is.EqualTo(ToolResult.Success));
        Assert.That(repeated.Return.Message, Is.EqualTo("ab ab ab"));

        Assert.That(((MessageTurn)turns[4]).Message, Is.EqualTo("ab ab ab"));
        Assert.That(((TurnEnd)turns[6]).Reason, Is.EqualTo(StopReason.EndTurn));
    }

    [Test]
    public void TokensComeFromTheResultOnly()
    {
        List<ITurn> turns = new Replay().Run("claude-tools.jsonl");

        UsageTurn usage = turns.OfType<UsageTurn>().Single();
        Assert.That(usage.InputTokens, Is.EqualTo(6 + 3588 + 10163));
        Assert.That(usage.OutputTokens, Is.EqualTo(189));
        Assert.That(usage.TokenCount, Is.EqualTo(13946));
        Assert.That(turns.Where(t => t is not UsageTurn).Select(t => t.TokenCount), Is.All.Null);
    }

    [Test]
    public void TurnLimitIsAnErrorEvenThoughTheModelStoppedForATool()
    {
        List<ITurn> turns = new Replay().Run("claude-max-turns.jsonl");

        Assert.That(turns.Select(t => t.GetType()), Is.EqualTo(new[] { typeof(ToolTurn), typeof(ToolResultTurn), typeof(UsageTurn), typeof(TurnEnd) }));

        Assert.That(turns[2].TokenCount, Is.EqualTo(4514), "A failed run was still billed.");

        TurnEnd end = (TurnEnd)turns[3];
        Assert.That(end.Reason, Is.EqualTo(StopReason.Error));
        Assert.That(end.Success, Is.False);
        Assert.That(end.Data, Is.EqualTo("Claude stopped early (error_max_turns)."));
    }

    [Test]
    public void SessionIdIsAdoptedOnlyFromInit()
    {
        Replay replay = new();

        replay.Parser.Parse("""{"type":"system","subtype":"thinking_tokens","session_id":"7d21f8bb-d602-48b2-89bd-d5fcf6b0d6c0"}""");
        Assert.That(replay.SessionId, Is.Null);

        replay.Parser.Parse("""{"type":"system","subtype":"init"}""");
        Assert.That(replay.SessionId, Is.EqualTo(Launched), "An init without a usable id falls back to the id Rhino launched with.");
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("""{"type":"assistant","message":""")]
    [TestCase("[1, 2]")]
    [TestCase("""{"type":5}""")]
    [TestCase("""{"type":"result","usage":{"input_tokens":1.5}}""")]
    public void BadLineIsSkippedAndTheStreamCarriesOn(string line)
    {
        Replay replay = new();
        string reply = Lines("claude-reply.jsonl")[1];

        Assert.That(replay.Parser.Parse(line), Is.Empty);
        Assert.That(replay.Parser.Parse(reply), Has.Count.EqualTo(1));
    }

    public static IReadOnlyList<string> Lines(string name)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Recordings", name));

}
