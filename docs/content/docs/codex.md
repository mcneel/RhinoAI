---
title: Codex
weight: 4
prev: docs/getting-started
next: docs/try-it-out
toc: false
---

[Codex](https://github.com/openai/codex) is OpenAI's terminal-based AI
assistant. It speaks MCP, so once you point it at the Rhino server it can
drive Rhino the same way Claude can.

If you're choosing between assistants and aren't sure, start with [Claude
Desktop](../connector) &mdash; it's the gentler entry point.

## Before you start

1. The **Rhino-MCP-Platform** plugin is installed in Rhino. See
   [Getting Started](../getting-started) if you haven't done that yet.
2. **Codex** is installed and signed in. See the
   [Codex install guide](https://github.com/openai/codex#installation)
   if you need it.

## Wire up the Rhino server

1. In Rhino, run the `RhinoMCPConnect` command. It prints the command
   Codex needs to launch the Rhino MCP router.
2. Open `~/.codex/config.toml` (create it if it doesn't exist).
3. Add an entry for the Rhino server, pasting the command and args from
   step 1:

   ```toml
   [mcp_servers.rhino]
   command = "rhino-mcp-router"
   args = ["--default-version", "8"]
   ```

4. Restart Codex. The `rhino` server should appear when you list MCP
   servers from inside a session.

> **Pick the Rhino version** by changing the `--default-version` arg.
> Use `8` for Rhino 8, `9` for Rhino 9 WIP.

## Try it out

Start a Codex session and follow the prompts on the
[Try it out](../try-it-out) page.
