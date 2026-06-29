---
title: OpenAI Codex
icon: chatgpt
weight: 5
prev: docs/getting-started
next: docs/try-it-out
toc: false
author: Callum
editor: SteveF
keywords:
  - Codex
  - OpenAI
  - terminal
  - CLI
---

[Codex](https://github.com/openai/codex) is OpenAI's terminal-based AI assistant. It speaks MCP, so once you point it at the Rhino MCP server, it can drive Rhino & Grasshopper the same way Claude can.

If you're choosing between assistants and aren't sure, start with [Claude Desktop](../connector); it's the gentler entry point.

## 1. Install Codex

[Codex](https://github.com/openai/codex) — install and sign in. See the [Codex install guide](https://github.com/openai/codex#installation) if you need it.

## 2. Install the Rhino plugin

{{< yak package="Rhino-MCP-Platform" version="8" >}}
{{< yak package="Rhino-MCP-Platform" version="9" >}}

If that doesn't work you can try the below:

1. Open Rhino 8 (and/or Rhino 9 WIP)
2. Run the `PackageManager` command
3. Search for, and install Rhino-MCP-Platform

## 3. Wire up the Rhino MCP server

1. In Rhino, run the `MCPConnect` command. It prints the command Codex needs to launch the Rhino MCP router.
2. Open `~/.codex/config.toml` (create it if it doesn't exist).
3. Add an entry for the Rhino server, pasting the command and args from step 1:

   ```toml
   [mcp_servers.rhino]
   command = "rhino-mcp-router"
   args = ["--default-version", "8"]
   ```

4. Restart Codex. The `rhino` server should appear when you list MCP servers from inside a session.

> **Pick the Rhino version** by changing the `--default-version` arg.
> Use `8` for Rhino 8, `9` for Rhino 9 WIP/BETA.

## Try it out

<blockquote class="page-note">
Start a Codex session and follow the prompts on the <a href="../../try-it-out">Try It Out</a> page.
</blockquote>
