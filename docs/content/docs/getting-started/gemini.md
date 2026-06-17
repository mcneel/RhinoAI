---
title: Gemini CLI
icon: gemini
weight: 6
prev: docs/getting-started
next: docs/try-it-out
toc: false
author: SteveF
keywords:
  - Gemini CLI
  - Google
  - terminal
  - CLI
---

[Gemini CLI](https://github.com/google-gemini/gemini-cli) is Google's open-source terminal AI assistant. It speaks MCP, so once you point it at the Rhino MCP server it can drive Rhino & Grasshopper the same way Claude or Codex can.

If you're choosing between assistants and aren't sure, start with [Claude Desktop](../connector); it's the gentler entry point.

## Before you start

1. The **ai** plugin is installed in Rhino. See [Getting Started](../) if you haven't done that yet.
2. **Gemini CLI** is installed and signed in. See the [Gemini CLI install guide](https://github.com/google-gemini/gemini-cli#installation) if you need it.

## Wire up the Rhino MCP server

1. In Rhino, run the `MCPConnect` command. It prints the command Gemini CLI needs to launch the Rhino MCP router.
2. Open `~/.gemini/settings.json` (create it if it doesn't exist).
3. Add an `mcpServers` entry for the Rhino server, pasting the command and args from step 1:

   ```json
   {
     "mcpServers": {
       "rhino": {
         "command": "rhino-mcp-router",
         "args": ["--default-version", "8"]
       }
     }
   }
   ```

4. Restart Gemini CLI. The `rhino` server should appear when you list MCP servers from inside a session.

> **Pick the Rhino version** by changing the `--default-version` arg.
> Use `8` for Rhino 8, `9` for Rhino 9 WIP.

## Try it out

Start a Gemini CLI session and follow the prompts on the [Try it out](../../try-it-out) page.
