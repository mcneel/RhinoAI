using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Drives Claude via the local CLI against an isolated rhino-mcp-router. No
// Rhino install required — close_slot on a bogus slot only touches the router.
[TestFixture]
[McpDependency("rhino")]
public sealed class CloseSlotAgentTests : AgenticTestBase
{
    private string _isolatedTempDir = null!;

    protected override void ConfigureHarness()
    {
        _isolatedTempDir = RhinoRouterPaths.CreateIsolatedTempDir();
        UseMcp(
            name: "rhino",
            command: RhinoRouterPaths.ResolveBinary(),
            env: RhinoRouterPaths.IsolatedEnv(_isolatedTempDir));
        UseDefaults(maxBudgetUsd: 0.25);
    }

    [OneTimeTearDown]
    public void CleanupTempDir()
    {
        RhinoRouterPaths.TryDeleteDirectory(_isolatedTempDir);
    }

    [Test]
    public async Task agent_receives_slot_not_found_when_closing_unknown_slot()
    {
        AgentRun run = await Agent
            .WithAllowedTools("mcp__rhino__close_slot")
            .WithBuiltinTools("")
            .WithWorkingDirectory(_isolatedTempDir)
            .WithSystemPrompt(
                "You are an integration test harness. Call exactly the tools requested. " +
                "When reporting results, include the raw JSON the tool returned.")
            .RunAsync(
                "Close the rhino slot named 'made-up-slot-xyz'. " +
                "Do not call list_slots first — just attempt the close once and report what the tool returned.");

        Assert.That(run.Metadata?["is_error"], Is.EqualTo(false),
            $"Claude reported an error. Payload: {run.Metadata?["error_payload"]}\n" +
            $"Stderr:\n{run.Metadata?["stderr"]}");

        Assert.That(run, Did.CallTool("mcp__rhino__close_slot"));

        ToolCall close = run.ToolCalls.First(c => c.Name == "mcp__rhino__close_slot");

        // The JSON property assertions auto-unwrap the MCP content envelope,
        // so we don't have to care whether the harness flattened it for us.
        // Failure envelopes carry a structured `error: { code, message }`.
        Assert.That(close.Result, Json.HasProperty("error", Json.HasProperty("code", Is.EqualTo("slot_not_found"))));
    }
}
