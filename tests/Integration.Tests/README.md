# Integration.Tests

End-to-end tests that drive the rhino MCP router directly over MCP (no LLM in the loop).

## Pre-requisites

- Build the router binary: `dotnet build rhino/plugin/RhinoAI.csproj -c Release -p:RhinoTarget=R9 -p:IsInRhino=false`

## Running

```sh
dotnet test tests/Integration.Tests
```

## How it works

The harness under `Harness/` spawns a freshly-built router binary in an isolated TMPDIR and calls its tools through an MCP client. See [Harness/RouterFixture.ai.cs](Harness/RouterFixture.ai.cs) for the canonical pattern.
