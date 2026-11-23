# Tests

| Project | Covers | Needs Rhino | In CI |
|---|---|---|---|
| `Router.Tests` | Router config, Rhino locator, slot store, spawn diagnostics, codegen | No | Yes |
| `Server.Tests` | Plug-in MCP server: protocol, schema builder, parameter binding, result formatting, config editing | No | Yes |
| `sdk.tests` | SDK agents, loop, tools, permissions, secrets, MCP clients | No | Yes (Ubuntu) |
| `launcher` | `shared/router-launcher.mjs` against a published router | No | Yes |
| `Integration.Tests` | Router ↔ Rhino end to end: spawning, slots, crashes, doc lifecycle, Grasshopper | **Yes** | No |
| `StdioTestServer` | Helper MCP server used by `sdk.tests` | | |

## Running

```sh
dotnet test tests/Router.Tests -c Release
dotnet test tests/Server.Tests -c Release
dotnet test tests/sdk.tests -c Release -p:ExposeInternalsToTests=true
(cd tests/launcher && node --test)
```

### Integration tests

Build the plug-in first so a router binary exists, then run with Rhino installed:

```sh
dotnet build rhino/plugin/RhinoAI.csproj -c Release -p:RhinoTarget=R9 -p:IsInRhino=false
dotnet test tests/Integration.Tests
```

The harness in `Integration.Tests/Harness/` starts the router in an isolated temp directory and calls tools through an MCP client, with no model in the loop. `RouterFixture.ai.cs` is the pattern to copy for new tests.

## Notes

- `Server.Tests` compiles plug-in sources by path. If you add or rename a linked file, update the `<Compile Include>` list in its csproj, including the `.ai.cs` suffix.
- `RhinoUiShim.ai.cs` and `StubServices.ai.cs` stand in for RhinoCommon so server tests run without Rhino.
- `sdk.tests` tests marked `Category=Manual` drive local desktop apps and are excluded from CI.
