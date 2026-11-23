# RhinoAI.Sdk

A .NET library for building agents that work inside Rhino: model adapters, a tool-calling loop, MCP clients, skills, and permissions. Package ID `RhinoAI`, assembly `RhinoAI.Sdk`. Targets `net8.0` (plus `net48` on Windows).

> Status: pre-1.0. Public API will change. Ships with Rhino 9 only.

## Pieces

| Type / folder | Role |
|---|---|
| `Agent` | A conversation: holds turns, a model definition, and a harness. `SendAsync` / `StreamAsync` run a turn. |
| `Harness` | Registers MCPs, skills, and permissions, and runs the agent loop (`LoopAsync`). `RhinoHarness` adds Rhino-specific prompts and tools. |
| `Models/` | Adapters per vendor and transport: Claude, ChatGPT, Gemini, DeepSeek, local models, and desktop/CLI apps (Claude Desktop, Codex). |
| `Mcps/` | MCP clients over stdio and HTTP, a built-in tool set (read, write, edit, resources, skills, URLs), and an in-memory MCP. |
| `Permissions/` | Glob- and argument-based rules that decide which tool calls run without asking. |
| `Secrets/` | API key storage: Windows Credential Manager, macOS Keychain, Linux Secret Service. |
| `Prompts/` | System prompts used by `RhinoHarness`. |
| `PlugIns/` | Tokens that identify the calling Rhino plug-in. |
| `AIInstances` | Process-wide registry of agent definitions and user preferences. |

## Usage

### Register your plug-in

A third-party plug-in registers once and gets a `PlugInToken`. Every SDK call takes it. The calling assembly needs an `[assembly: Guid]` attribute.

```csharp
PlugInToken? token = PlugInRegistry.Register();
```

### Extend the AI panel

Register skills so the panel's agent knows about your plug-in, and MCPs to give it more tools.

```csharp
PlugInRegistry.RegisterPanelSkill(token, skill);

MemoryMcp mcp = new("MyPluginsMcp");
mcp.RegisterTool(myTool);
PlugInRegistry.RegisterPanelMcp(token, mcp);
```

### Run your own agent

`GetPreferredAgent` returns an agent built from the user's preferred model, harness, and config. It is separate from the AI panel's agent and can run alongside it.

```csharp
Agent? agent = Agent.GetPreferredAgent(token);
IAgentResult result = await agent.SendAsync("Make a 10 x 10 x 10 box at the origin", ct);

// or stream
StreamedAgentResult stream = agent.StreamAsync("Summarise the selected objects", ct);
```

### Permissions

Your token is tied to a permission set the user approves. The first `SendAsync` fails until the user approves your plug-in in AI Settings. After that, individual tool calls are approved according to the user's permission rules.

### Models, harnesses, MCPs

- **Models** are the agent's brains: API models, desktop/CLI apps, and local models.
- **Harnesses**: desktop models (Claude Desktop, Codex) bring their own harness and use it. API and local models run inside `Harness` or a custom subclass.
- **MCPs** are the toolboxes an agent uses inside its harness.

The plug-in in [`../plugin`](../plugin) is the reference consumer.

### Outside Rhino

The SDK is designed for use inside Rhino today. Using it from outside Rhino (for example through the router) is an open question. Open an issue if you need that.

## Building and testing

```sh
dotnet build sdk.csproj -c Release
dotnet test ../../tests/sdk.tests -c Release -p:ExposeInternalsToTests=true
```

`tests/sdk.tests` needs .NET 10. On Linux, the Secret Service tests need an unlocked keyring and D-Bus session. Tests in `Category=Manual` drive locally installed desktop apps and are skipped in CI. Some model tests read API keys (e.g. `DEEPSEEK_API_KEY`) from the environment and skip without them.

## Adding a model

1. Add a class under `Models/` implementing `IModel`.
2. Map the vendor's message and tool-call format onto the SDK `ITurn` types in `Models/Turns`.
3. Add a definition to `Definitions.json`.
4. Add tests in `tests/sdk.tests`. Mark anything that needs a live key or local app as `Category=Manual`.
