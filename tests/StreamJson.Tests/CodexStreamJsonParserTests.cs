using Acp;
using RhMcp;

namespace RhMcp.StreamJson.Tests;

// Canned-line tests for the Codex (`codex exec --experimental-json`) translation. These pin our
// internal parser contract; they do NOT prove the contract matches the shipped binary (that is a
// separate manual live check, per the design's codexApproach caveat).
[TestFixture]
public sealed class CodexStreamJsonParserTests
{
    private static CodexStreamJsonParser NewParser() =>
        new(new AgentDefinition(
            Name: "codex",
            Adapter: AgentAdapter.Codex,
            Command: "codex",
            SearchPaths: [],
            Model: "",
            ExtraArgs: [],
            SystemPrompt: "",
            Enabled: true,
            IsBuiltin: true));

    [Test]
    public void Session_configured_is_none_session_start_is_the_runners_concern()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"msg":{"type":"session_configured","session_id":"abc"}}""");

        Assert.That(parsed.IsTurnComplete, Is.False);
        Assert.That(parsed.Updates, Is.Empty);
    }

    [Test]
    public void Agent_message_emits_one_agent_message_chunk()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"msg":{"type":"agent_message","message":"working on it"}}""");

        Assert.That(parsed.IsTurnComplete, Is.False);
        Assert.That(parsed.Updates, Has.Count.EqualTo(1));
        AgentMessageChunkSessionUpdate chunk = (AgentMessageChunkSessionUpdate)parsed.Updates[0];
        Assert.That(((TextContentBlock)chunk.Content).Text, Is.EqualTo("working on it"));
    }

    [Test]
    public void Mcp_tool_call_emits_tool_call_titled_by_tool_name()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"msg":{"type":"mcp_tool_call","tool":"add_box","server":"rhino"}}""");

        Assert.That(parsed.IsTurnComplete, Is.False);
        Assert.That(parsed.Updates, Has.Count.EqualTo(1));
        ToolCallSessionUpdate call = (ToolCallSessionUpdate)parsed.Updates[0];
        Assert.That(call.ToolCallId, Is.EqualTo("add_box"));
        Assert.That(call.Title, Is.EqualTo("add_box"));
    }

    [Test]
    public void Task_complete_is_the_terminal_event_and_ends_the_turn()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"msg":{"type":"task_complete","last_agent_message":"all done"}}""");

        Assert.That(parsed.IsTurnComplete, Is.True);
        Assert.That(parsed.Reason, Is.EqualTo(StopReason.EndTurn));
        Assert.That(parsed.Updates, Is.Empty);
        Assert.That(parsed.Usage.IsEmpty, Is.True); // no usage object -> Empty, never faulted
    }

    [Test]
    public void Task_complete_surfaces_token_usage_without_cost()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"msg":{"type":"task_complete","last_agent_message":"done","usage":{"input_tokens":200,"output_tokens":90}}}""");

        Assert.That(parsed.Usage.InputTokens, Is.EqualTo(200));
        Assert.That(parsed.Usage.OutputTokens, Is.EqualTo(90));
        Assert.That(parsed.Usage.CostUsd, Is.Null); // Codex reports tokens only
    }

    [Test]
    public void Unknown_event_type_is_fail_soft_none()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"msg":{"type":"token_count","input_tokens":42}}""");

        Assert.That(parsed.IsTurnComplete, Is.False);
        Assert.That(parsed.Updates, Is.Empty);
    }

    [Test]
    public void Junk_line_without_type_is_none()
    {
        ParsedLine parsed = NewParser().Parse("""{"unrelated":"shape"}""");

        Assert.That(parsed.IsTurnComplete, Is.False);
        Assert.That(parsed.Updates, Is.Empty);
    }
}
