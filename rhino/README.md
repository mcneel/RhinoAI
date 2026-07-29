# Rhino MCP Server Plugin

The Rhino MCP Server works in a unique way. The MCP Link and all requests are sent to the router app. 

The router app communicates to AI Agents via IO rather than HTTP. This has several benefits
- More stable connection
- No socket interference
- The MCP Server picks it up immediately, no reconnect needed
- The router can launch as many Rhino instances for us as we need

## Grasshopper AI Tools bridge (fork addition, not upstream)

`gh_list_tools` and `gh_run_tool` (`plugin/Tools/GhBridgeTools.cs`) are gateway tools onto
the **GrasshopperAITools** Rhino plug-in, which is a separate product with its own
installer. This plugin is a **pure client**: it opens the named pipe
`grasshopper-aitools-bridge` and speaks a small framed-JSON protocol over it
(`plugin/Server/GhBridge/`). There is no project reference and no shared assembly between
the two; the pipe name is the whole contract. If that plug-in is not installed or is
disabled, both tools return `{ "error": …, "hint": … }` rather than throwing — a missing
bridge must never take the MCP server down.

That plug-in owns the tool library entirely: discovery, semantic ranking (hence
`gh_list_tools`' `query` is a meaning-based search, not a substring filter), and
execution. Grasshopper does **not** need to be open — it is loaded on demand, which is why
the first `gh_run_tool` of a session can take up to two minutes.

Environment variables:

| Variable | Read by | Meaning |
|---|---|---|
| `RHINO_MCP_GH_BRIDGE_TIMEOUT_MS` | this plugin | Per-request timeout in ms. Default `180000`; it must stay above the far side's cold-Grasshopper-load budget of 120 s. |
| `GRASSHOPPER_AITOOLS_DIR` | the **GrasshopperAITools** plug-in | Where the published `.aitool` bundles live. |

`RHINO_MCP_GH_TOOLS_DIR` **no longer exists.** It belonged to the removed in-process
`GhToolSource`, which loaded and executed bundles itself. Nothing on this side of the pipe
resolves a tools folder any more — set `GRASSHOPPER_AITOOLS_DIR` instead, and note that it
is read by the *other* process, so it must be set where Rhino sees it.
