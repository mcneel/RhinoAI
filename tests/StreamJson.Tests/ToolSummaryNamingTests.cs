namespace RhinoAI.StreamJson.Tests;

// Agents call our tools through MCP, so every name arrives namespaced as mcp__<server>__<tool>.
// Every phrase in ToolSummary is keyed on the registered name, so the prefix has to come off first
// or nothing matches and each call is labelled with its own raw name twice over.
[TestFixture]
public sealed class ToolSummaryNamingTests
{
    [Test]
    public void The_mcp_prefix_is_stripped()
    {
        Assert.That(ToolSummary.Bare("mcp__rhino__run_python"), Is.EqualTo("run_python"));
        Assert.That(ToolSummary.Bare("mcp__rhino__g2_place_component"), Is.EqualTo("g2_place_component"));
    }

    [Test]
    public void A_server_name_containing_underscores_still_resolves()
    {
        Assert.That(ToolSummary.Bare("mcp__my_server__run_python"), Is.EqualTo("run_python"));
    }

    [Test]
    public void An_unprefixed_or_malformed_name_is_left_alone()
    {
        Assert.That(ToolSummary.Bare("run_python"), Is.EqualTo("run_python"));
        Assert.That(ToolSummary.Bare("mcp__nothingfollows"), Is.EqualTo("mcp__nothingfollows"));
        Assert.That(ToolSummary.Bare(string.Empty), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Namespaced_tools_get_their_real_phrase_rather_than_their_own_name()
    {
        Assert.That(ToolSummary.Describe("mcp__rhino__run_python", "{}", "{\"Ok\":true}"), Is.EqualTo("ran python"));
        Assert.That(
            ToolSummary.Describe("mcp__rhino__g2_place_component", "{\"selector\":\"Circle\"}", "{\"Ok\":true}"),
            Is.EqualTo("placed Circle"));
        Assert.That(ToolSummary.Describe("mcp__rhino__get_viewport_image", "{}", "{}"), Is.EqualTo("captured viewport"));
    }

    [Test]
    public void A_failure_verb_also_sees_past_the_prefix()
    {
        Assert.That(
            ToolSummary.Describe("mcp__rhino__run_python", "{}", "{\"Ok\":false}"),
            Is.EqualTo("python failed"));
        Assert.That(
            ToolSummary.Describe("mcp__rhino__g2_solve_canvas", "{}", "{\"error\":\"boom\"}"),
            Is.EqualTo("Grasshopper failed"));
    }

    [Test]
    public void An_unknown_tool_still_falls_back_to_its_bare_name()
    {
        Assert.That(ToolSummary.Describe("mcp__github__create_issue", "{}", "{}"), Is.EqualTo("create_issue: ok"));
    }
}
