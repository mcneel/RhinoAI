using Acp;
using RhMcp;

namespace RhMcp.StreamJson.Tests;

// Canned-line tests for the Claude Code stream-json translation. Each test feeds one real-shaped
// stdout line and asserts the emitted SessionUpdate kinds/fields, plus the terminal/junk contract.
[TestFixture]
public sealed class ClaudeStreamJsonParserTests
{
    private static ClaudeStreamJsonParser NewParser() =>
        new(new AgentDefinition(
            Name: "claude",
            Adapter: AgentAdapter.Claude,
            Command: "claude",
            SearchPaths: [],
            Model: "",
            ExtraArgs: [],
            SystemPrompt: "",
            Enabled: true,
            IsBuiltin: true));

    [Test]
    public void Assistant_text_emits_one_agent_message_chunk()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"hi there"}]}}""");

        Assert.That(parsed.IsTurnComplete, Is.False);
        Assert.That(parsed.Updates, Has.Count.EqualTo(1));
        AgentMessageChunkSessionUpdate chunk = (AgentMessageChunkSessionUpdate)parsed.Updates[0];
        Assert.That(((TextContentBlock)chunk.Content).Text, Is.EqualTo("hi there"));
    }

    [Test]
    public void Assistant_tool_use_emits_tool_call_with_id_title_and_raw_input()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","id":"toolu_1","name":"mcp__rhino__add_box","input":{"x":1}}]}}""");

        Assert.That(parsed.IsTurnComplete, Is.False);
        Assert.That(parsed.Updates, Has.Count.EqualTo(1));
        ToolCallSessionUpdate call = (ToolCallSessionUpdate)parsed.Updates[0];
        Assert.That(call.ToolCallId, Is.EqualTo("toolu_1"));
        Assert.That(call.Title, Is.EqualTo("mcp__rhino__add_box"));
        Assert.That(call.RawInput, Is.Not.Null);
        Assert.That(call.RawInput!.Value.GetProperty("x").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public void User_tool_result_emits_completed_tool_call_update_with_raw_output()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"done"}]}}""");

        Assert.That(parsed.IsTurnComplete, Is.False);
        Assert.That(parsed.Updates, Has.Count.EqualTo(1));
        ToolCallUpdateSessionUpdate update = (ToolCallUpdateSessionUpdate)parsed.Updates[0];
        Assert.That(update.ToolCallId, Is.EqualTo("toolu_1"));
        Assert.That(update.Status, Is.EqualTo(ToolCallStatus.Completed));
        Assert.That(update.RawOutput, Is.Not.Null);
        Assert.That(update.RawOutput!.Value.GetString(), Is.EqualTo("done"));
    }

    [Test]
    public void Result_line_is_the_terminal_event_and_ends_the_turn()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"type":"result","subtype":"success","is_error":false,"result":"all done"}""");

        Assert.That(parsed.IsTurnComplete, Is.True);
        Assert.That(parsed.Reason, Is.EqualTo(StopReason.EndTurn));
        Assert.That(parsed.Updates, Is.Empty);
        Assert.That(parsed.Usage.IsEmpty, Is.True); // no usage object -> Empty, not faulted
    }

    [Test]
    public void Result_line_surfaces_token_usage_and_cost()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"type":"result","subtype":"success","is_error":false,"result":"done","total_cost_usd":0.0123,"usage":{"input_tokens":1500,"output_tokens":640}}""");

        Assert.That(parsed.IsTurnComplete, Is.True);
        Assert.That(parsed.Usage.InputTokens, Is.EqualTo(1500));
        Assert.That(parsed.Usage.OutputTokens, Is.EqualTo(640));
        Assert.That(parsed.Usage.TotalTokens, Is.EqualTo(2140));
        Assert.That(parsed.Usage.CostUsd, Is.EqualTo(0.0123m));
    }

    [Test]
    public void Result_line_without_cost_is_tokens_only()
    {
        ParsedLine parsed = NewParser().Parse(
            """{"type":"result","subtype":"success","is_error":false,"result":"done","usage":{"input_tokens":10,"output_tokens":5}}""");

        Assert.That(parsed.Usage.TotalTokens, Is.EqualTo(15));
        Assert.That(parsed.Usage.CostUsd, Is.Null);
        Assert.That(parsed.Usage.IsEmpty, Is.False);
    }

    [Test]
    public void Unknown_event_type_is_fail_soft_none()
    {
        ParsedLine parsed = NewParser().Parse("""{"type":"system","subtype":"init","cwd":"/tmp"}""");

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
