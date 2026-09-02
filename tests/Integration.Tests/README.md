# Integration.Tests

End-to-end tests that drive a real Claude session against the rhino MCP router.

## Pre-requisites

- Login to Claude Code locally: `claude /login`
- Build the router binary: `dotnet build rhino/plugin/RhinoAI.csproj -c Release -p:RhinoTarget=R9`

## Running

Agent-driven tests are marked `[Explicit]` and `[Category("AgentDriven")]` so they
do not run by default — they cost real subscription quota and are non-deterministic.

Opt in with:

```sh
dotnet test tests/Integration.Tests --filter "Category=AgentDriven"
```

Direct MCP tests (no LLM in the loop) run normally:

```sh
dotnet test tests/Integration.Tests
```

## How it works

The harness under `Harness/` spawns the local `claude` CLI in headless mode
(`-p --output-format stream-json`), hands it an inline MCP config that points
at a freshly-built router binary in an isolated TMPDIR, and parses the
stream-json output into a trajectory of tool calls + results.

See [Harness/ClaudeAgent.cs](Harness/ClaudeAgent.cs) and
[CloseSlotAgentTests.cs](CloseSlotAgentTests.cs) for the canonical pattern.
