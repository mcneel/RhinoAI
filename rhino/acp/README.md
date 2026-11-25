# Acp

A C# implementation of the [Agent Client Protocol](https://github.com/zed-industries/agent-client-protocol) (ACP). The plug-in uses it to host coding agents (Claude, Codex, Gemini) inside Rhino and stream their turns into the AI panel.

## Layout

| Path | Contents |
|---|---|
| `src/ClientSideConnection`, `src/AgentSideConnection` | The two ends of an ACP session |
| `src/JsonRpc/` | JSON-RPC 2.0 endpoint, messages, request IDs |
| `src/Transport/` | `StdioTransport` for child processes, `InMemoryTransport` for tests |
| `src/Generated/` | Protocol types, enums, unions, and method names generated from the schema. Do not edit. |
| `schema/` | `schema.json` and `meta.json` vendored from upstream ACP. Pinned tag in `schema/SOURCE.md`. |
| `codegen/` | Generator that turns `schema/` into `src/Generated/` |

## Updating the protocol version

1. Update the tag and commit in `schema/SOURCE.md`.
2. Download `schema.json` and `meta.json` at that tag into `schema/`.
3. From the repo root: `dotnet run --project rhino/acp/codegen`
4. Build the plug-in and review the `src/Generated/` diff before committing.

Generated files are LF-only; `.gitattributes` keeps them that way on Windows.
