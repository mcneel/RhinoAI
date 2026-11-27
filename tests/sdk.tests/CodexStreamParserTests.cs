using System.Text.Json.Nodes;

using Rhino.AI;
using Rhino.AI.Models;
using Rhino.AI.PlugIns;

using static sdk.tests.TestUtils;

namespace sdk.tests;

public class CodexStreamParserTests
{

    private sealed class Replay
    {
        public List<CodexRateLimits> RateLimits { get; } = [];

        public CodexStreamParser Parser { get; }

        public Replay()
        {
            Harness harness = new(PlugInToken.Invalid);
            MemoryMcp mcp = new("Test");
            mcp.RegisterTool(new TestTool("repeat", "", [
                new ToolParameter("text", "", ToolArgType.String, true),
                new ToolParameter("count", "", ToolArgType.Integer, true),
                new ToolParameter("separator", "", ToolArgType.String, false),
            ]));
            harness.AddMcp(mcp);

            Parser = new(harness, new Dictionary<string, Mcp> { ["Test"] = mcp }, RateLimits.Add);
        }

        public List<ITurn> Run(string recording)
        {
            List<ITurn> turns = [];
            foreach (string line in Lines(recording))
            {
                if (JsonNode.Parse(line) is not JsonObject message) continue;
                if (message["id"] is not null || message["method"]?.GetValue<string>() is not string method) continue;

                if (Parser.Parse(method, message["params"] as JsonObject ?? []) is ITurn turn)
                    turns.Add(turn);
            }

            return turns;
        }
    }

    private static JsonObject Params(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Test]
    public void PlainReply()
    {
        Replay replay = new();
        List<ITurn> turns = replay.Run("codex-reply.jsonl");

        Assert.That(turns.Select(t => t.GetType()), Is.EqualTo(new[] { typeof(TurnStart), typeof(MessageTurn), typeof(TurnEnd) }));

        MessageTurn reply = (MessageTurn)turns[1];
        Assert.That(reply.Role, Is.EqualTo(RoleType.Assistant));
        Assert.That(reply.Message, Is.EqualTo("hello from Rhino"));

        TurnEnd end = (TurnEnd)turns[2];
        Assert.That(end.Reason, Is.EqualTo(StopReason.EndTurn));
        Assert.That(end.TokenCount, Is.EqualTo(8211 + 7));

        CodexRateLimits limits = replay.RateLimits.Single();
        Assert.That(limits.Primary, Is.EqualTo(new CodexRateLimitWindow(9, 300, DateTimeOffset.FromUnixTimeSeconds(1795813573))));
        Assert.That(limits.Secondary, Is.EqualTo(new CodexRateLimitWindow(3, 10080, DateTimeOffset.FromUnixTimeSeconds(1796350759))));
        Assert.That(limits.PlanType, Is.EqualTo("plus"));
        Assert.That(limits.LimitReached, Is.Null);
    }

    [Test]
    public void FailedAndSucceededToolCalls()
    {
        List<ITurn> turns = new Replay().Run("codex-tools.jsonl");

        Assert.That(turns.Select(t => t.GetType()), Is.EqualTo(new[]
        {
            typeof(TurnStart),
            typeof(ThinkingTurn), typeof(ThinkingTurn),
            typeof(ToolTurn), typeof(ToolResultTurn),
            typeof(ToolTurn), typeof(ToolResultTurn),
            typeof(ThinkingTurn),
            typeof(MessageTurn), typeof(TurnEnd),
        }));

        Assert.That(turns[1].Data, Is.EqualTo("**Planning deferred test discovery**"));

        ToolTurn failedCall = (ToolTurn)turns[3];
        Assert.That(failedCall.Id, Is.EqualTo("exec-91638662-d907-4277-a2da-6dfbc101a2fd"));
        Assert.That(failedCall.Name, Is.EqualTo(ToolSchema.WireName("Test", "repeat")));
        Assert.That(failedCall.Args.OfType<ToolInt>().Single().Value, Is.EqualTo(-1), "The declared Integer type should win over JSON's plain number.");

        ToolResultTurn failed = (ToolResultTurn)turns[4];
        Assert.That(failed.Id, Is.EqualTo(failedCall.Id));
        Assert.That(failed.Return.Result, Is.EqualTo(ToolResult.Failure));
        Assert.That(failed.Return.Message, Is.EqualTo("An error occurred invoking 'repeat'."));

        Assert.That(((ToolTurn)turns[5]).Args.OfType<ToolInt>().Single().Value, Is.EqualTo(3));

        ToolResultTurn succeeded = (ToolResultTurn)turns[6];
        Assert.That(succeeded.Return.Result, Is.EqualTo(ToolResult.Success));
        Assert.That(succeeded.Return.Message, Is.EqualTo("ab ab ab"));

        Assert.That(((MessageTurn)turns[8]).Message, Is.EqualTo("ab ab ab"));
        Assert.That(((TurnEnd)turns[9]).Reason, Is.EqualTo(StopReason.EndTurn));
    }

    [Test]
    public void WarnsOnceWhenALimitIsNearlyUsed()
    {
        Replay replay = new();
        const string nearlyUsed = """{"rateLimits":{"primary":{"usedPercent":85,"windowDurationMins":300},"secondary":null,"planType":"plus","rateLimitReachedType":null}}""";

        ITurn? first = replay.Parser.Parse("account/rateLimits/updated", Params(nearlyUsed));
        ITurn? second = replay.Parser.Parse("account/rateLimits/updated", Params(nearlyUsed));

        Assert.That(first, Is.TypeOf<MessageTurn>());
        Assert.That(first!.Role, Is.EqualTo(RoleType.System));
        Assert.That(first.Data, Is.EqualTo("Codex usage: 85% of your 5-hour limit used."));
        Assert.That(second, Is.Null);
        Assert.That(replay.RateLimits, Has.Count.EqualTo(2));
    }

    [TestCase("""{"turn":{"status":"interrupted"}}""", StopReason.Cancelled, "")]
    [TestCase("""{"turn":{"status":"failed","error":{"message":"Model overloaded"}}}""", StopReason.Error, "Model overloaded")]
    [TestCase("""{"turn":{"status":"failed"}}""", StopReason.Error, "Codex turn failed.")]
    public void TurnCompletedCarriesItsStatus(string parameters, StopReason reason, string message)
    {
        TurnEnd end = (TurnEnd)new Replay().Parser.Parse("turn/completed", Params(parameters))!;

        Assert.That(end.Reason, Is.EqualTo(reason));
        Assert.That(end.Data, Is.EqualTo(message));
    }

    public static IReadOnlyList<string> Lines(string name)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Recordings", name));

}
