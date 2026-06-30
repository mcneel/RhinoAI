using System.IO;
using System.Threading.Tasks;
using Acp;
using RhMcp;

namespace RhMcp.StreamJson.Tests;

// Drives the real StreamJsonAgent over its injected stdin/stdout seam (no process, no CLI). One
// scripted Claude turn is replayed; we assert the framed prompt reaches stdin, the SessionUpdates
// flow through the real RhinoAcpClient into the Conversation, and the turn TCS resolves EndTurn.
[TestFixture]
public sealed class StreamJsonAgentLoopbackTests
{
    private static AgentDefinition ClaudeDef() =>
        new(
            Name: "claude",
            Adapter: AgentAdapter.Claude,
            Command: "claude",
            SearchPaths: [],
            Model: "",
            ExtraArgs: [],
            SystemPrompt: "",
            Enabled: true,
            IsBuiltin: true);

    [Test]
    [CancelAfter(10000)]
    public async Task One_scripted_claude_turn_records_events_and_resolves_end_turn()
    {
        Conversation conversation = new(Guid.NewGuid(), "claude", "Untitled");
        RhinoAcpClient client = new(conversation);
        StreamJsonAgent agent = new(ClaudeDef(), client, conversation, "/tmp", new ClaudeStreamJsonParser(ClaudeDef()));

        // AgentRunner owns BeginTurn/CompleteTurn in the real path; the loopback seam only drives the
        // inner IAcpAgent, so open the turn here so RhinoAcpClient.Record lands in a current turn.
        conversation.BeginTurn("draw a box");

        // The CLI's stdout for one turn: assistant text, a tool_use, its tool_result, then the
        // terminal result line. Newline-framed exactly like the real `claude` stream-json output.
        string scriptedStdout = string.Join('\n',
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"sure"}]}}""",
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","id":"toolu_1","name":"add_box","input":{"size":10}}]}}""",
            """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"created id 42"}]}}""",
            """{"type":"result","subtype":"success","is_error":false,"result":"done"}""");

        using StringReader stdout = new(scriptedStdout);
        StringWriter stdin = new();

        StopReason reason = await agent.RunLoopbackAsync(stdout, stdin, [new TextContentBlock { Text = "draw a box" }]);

        // The turn resolved on the terminal event.
        Assert.That(reason, Is.EqualTo(StopReason.EndTurn));

        // The framed prompt was written to stdin as Claude's single-line user envelope.
        string written = stdin.ToString().Trim();
        Assert.That(written, Does.Contain("\"type\":\"user\""));
        Assert.That(written, Does.Contain("draw a box"));

        // The Conversation ends with the expected Turn events: assistant text, a tool-use whose
        // result was folded back into it (matched by id), recorded through the real RhinoAcpClient.
        Assert.That(conversation.Turns, Has.Count.EqualTo(1));
        Turn turn = conversation.Turns[0];

        IReadOnlyList<TurnEvent> events = turn.Events;
        Assert.That(events, Has.Count.EqualTo(2));

        Assert.That(events[0].Kind, Is.EqualTo(TurnEventKind.AssistantText));
        Assert.That(events[0].Text, Is.EqualTo("sure"));

        Assert.That(events[1].Kind, Is.EqualTo(TurnEventKind.ToolUse));
        Assert.That(events[1].Text, Is.EqualTo("add_box"));
        Assert.That(events[1].Id, Is.EqualTo("toolu_1"));
        Assert.That(events[1].Result, Does.Contain("created id 42"));
    }
}
