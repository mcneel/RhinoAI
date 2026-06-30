using Acp;
using RhMcp;
using ContentBlock = Acp.ContentBlock;

namespace RhMcp.StreamJson.Tests;

// Parser edge cases not pinned by the canned-line happy-path suites: multi-block assistant lines,
// empty/degenerate content, FormatTurn shaping (Claude's JSON envelope vs Codex's plain text), and
// the malformed-field fail-soft paths. These keep the translators honest on the shapes the read loop
// actually hands them.
[TestFixture]
public sealed class StreamJsonParserEdgeCaseTests
{
    private static AgentDefinition Def(AgentAdapter adapter, string name) =>
        new(name, adapter, name, [], string.Empty, [], string.Empty, true, true);

    private static ClaudeStreamJsonParser Claude() => new(Def(AgentAdapter.Claude, "claude"));
    private static CodexStreamJsonParser Codex() => new(Def(AgentAdapter.Codex, "codex"));

    [Test]
    public void Claude_assistant_line_with_text_and_tool_use_emits_both_in_order()
    {
        ParsedLine parsed = Claude().Parse(
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"on it"},{"type":"tool_use","id":"t1","name":"add_box","input":{}}]}}""");

        Assert.That(parsed.Updates, Has.Count.EqualTo(2));
        Assert.That(parsed.Updates[0], Is.InstanceOf<AgentMessageChunkSessionUpdate>());
        ToolCallSessionUpdate call = (ToolCallSessionUpdate)parsed.Updates[1];
        Assert.That(call.ToolCallId, Is.EqualTo("t1"));
    }

    [Test]
    public void Claude_assistant_line_with_empty_content_is_none()
    {
        ParsedLine parsed = Claude().Parse(
            """{"type":"assistant","message":{"role":"assistant","content":[]}}""");

        Assert.That(parsed.Updates, Is.Empty);
        Assert.That(parsed.IsTurnComplete, Is.False);
    }

    [Test]
    public void Claude_user_line_with_no_tool_results_is_none()
    {
        ParsedLine parsed = Claude().Parse(
            """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"echo"}]}}""");

        Assert.That(parsed.Updates, Is.Empty);
    }

    [Test]
    public void Claude_format_turn_wraps_text_and_image_in_the_user_envelope()
    {
        ContentBlock[] prompt =
        [
            new TextContentBlock { Text = "look" },
            new ImageContentBlock { Data = "QUJD", MimeType = "image/png" },
        ];

        string framed = Claude().FormatTurn(prompt);

        using JsonDocument doc = JsonDocument.Parse(framed);
        JsonElement root = doc.RootElement;
        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("user"));
        JsonElement content = root.GetProperty("message").GetProperty("content");
        Assert.That(content.GetArrayLength(), Is.EqualTo(2));
        Assert.That(content[0].GetProperty("type").GetString(), Is.EqualTo("text"));
        Assert.That(content[1].GetProperty("type").GetString(), Is.EqualTo("image"));
        // Underscored property survives the camelCase policy.
        Assert.That(content[1].GetProperty("source").GetProperty("media_type").GetString(), Is.EqualTo("image/png"));
    }

    [Test]
    public void Codex_format_turn_joins_text_pieces_and_notes_an_omitted_image()
    {
        ContentBlock[] prompt =
        [
            new TextContentBlock { Text = "line one" },
            new ImageContentBlock { Data = "x", MimeType = "image/png" },
            new TextContentBlock { Text = "line two" },
        ];

        string framed = Codex().FormatTurn(prompt);

        Assert.That(framed, Does.StartWith("line one\n"));
        Assert.That(framed, Does.Contain("[image omitted"));
        Assert.That(framed, Does.EndWith("line two"));
    }

    [Test]
    public void Codex_agent_message_with_non_string_message_is_none()
    {
        ParsedLine parsed = Codex().Parse(
            """{"msg":{"type":"agent_message","message":{"nested":"object"}}}""");

        Assert.That(parsed.Updates, Is.Empty);
        Assert.That(parsed.IsTurnComplete, Is.False);
    }

    [Test]
    public void Codex_mcp_tool_call_without_a_tool_field_is_none()
    {
        ParsedLine parsed = Codex().Parse(
            """{"msg":{"type":"mcp_tool_call","server":"rhino"}}""");

        Assert.That(parsed.Updates, Is.Empty);
    }

    [Test]
    public void Codex_reads_type_from_root_when_there_is_no_msg_envelope()
    {
        ParsedLine parsed = Codex().Parse(
            """{"type":"task_complete","last_agent_message":"done"}""");

        Assert.That(parsed.IsTurnComplete, Is.True);
        Assert.That(parsed.Reason, Is.EqualTo(StopReason.EndTurn));
    }

    [Test]
    public void Claude_result_ignores_a_non_numeric_cost()
    {
        ParsedLine parsed = Claude().Parse(
            """{"type":"result","subtype":"success","is_error":false,"result":"done","total_cost_usd":"oops","usage":{"input_tokens":3,"output_tokens":2}}""");

        Assert.That(parsed.Usage.TotalTokens, Is.EqualTo(5));
        Assert.That(parsed.Usage.CostUsd, Is.Null); // a malformed cost degrades, never faults
    }
}
