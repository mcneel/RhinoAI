# RhinoAI: Gemini & Antigravity Runtime Integration

## Environment & Build Architecture
- Repository: Local clone at `C:\Users\Steven\source\repos\RhinoAI` on branch `feat/gemini-acp-test`.
- Upstream: Tracking `mcneel/RhinoAI` on branch `rhino-9.x`.
- Framework: C# / .NET 8 targeting Rhino 9 (`rhino/plugin/RhinoAI.csproj`, x64).
- Process Constraint: Rhino 9 locks `RhinoAI.rhp` in memory. All builds must occur with Rhino closed:
  `dotnet build rhino/plugin/RhinoAI.csproj -c Debug /p:Platform=x64`

## System Architecture
RhinoAI acts as an in-process Eto.Forms plugin inside Rhino 9 that orchestrates external AI agents via standard I/O streams using Model Context Protocol (MCP) and Agent Communication Protocol (ACP).
- `AgentHost.ai.cs`: Spawns the CLI runner subprocess (`agy` for Antigravity/Gemini).
- `GeminiConnection.ai.cs`: Establishes the transport bridge and prompt payload dispatch.
- `AntigravityStreamJsonParser.ai.cs`: Demultiplexes incoming stdout JSON lines into chat tokens and tool invocations.
- Local MCP Server: Exposes RhinoCommon and Grasshopper actions (geometry creation, viewport control, doc inspection) to the agent.

## Current Working Delta
The current branch contains modifications across 5 core files:
- `rhino/plugin/Agents/Definitions.json`: Added Gemini model declarations and `agy` runner definitions.
- `rhino/plugin/Agents/AgentDefinition.cs`: Configuration mapping arguments and command options.
- `rhino/plugin/Agents/AgentHost.ai.cs`: Subprocess lifecycle and CLI argument passing (removed deprecated `--acp` and `-mcp` flags).
- `rhino/plugin/Agents/GeminiConnection.ai.cs`: Connection initialization and payload handling.
- `rhino/plugin/Agents/Conversations/AntigravityStreamJsonParser.ai.cs`: Streaming parser for `agy` stream-json output.

## Immediate Objective
Fix Gemini's exploratory behavior: Gemini currently probes tools or chats prior to acting, whereas Claude and Codex execute Rhino MCP geometry tools deterministically on turn 1.

Tasks for this session:
1. System Prompt Anchoring:
   Inspect `GeminiConnection.ai.cs` and `AgentHost.ai.cs`. Ensure an explicit system directive is injected into the payload telling Gemini it is embedded inside Rhino 3D, must interact exclusively via exposed Rhino MCP tools, and must bypass conversational preamble.
2. Stream Parsing & Tool Routing:
   Audit `AntigravityStreamJsonParser.ai.cs` to ensure streaming `functionCall` chunks from `agy` are routed immediately to RhinoAI's tool dispatcher without leaking raw JSON to the UI.
3. Schema Validation:
   Inspect parameter definitions in `Definitions.json` and `AgentDefinition.cs` to verify all MCP tool definitions strictly declare explicit JSON schema types (`string`, `number`, `integer`, `boolean`, `array`, `object`) to prevent Gemini from probing argument boundaries.
4. Compilation & Verification:
   Verify zero C# compiler errors (`CS8336`, `CS1501`, `CS0103`) via `dotnet build`.
