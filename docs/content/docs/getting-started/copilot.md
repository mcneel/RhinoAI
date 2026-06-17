---
title: GitHub Copilot
icon: github
weight: 4
prev: docs/getting-started
next: docs/try-it-out
toc: false
author: SteveF
keywords:
  - GitHub Copilot
  - VS Code
  - MCP
  - agent mode
---

[GitHub Copilot](https://github.com/features/copilot) in VS Code can act as an MCP client when run in **agent mode**. Point it at the Rhino MCP server and Copilot can drive Rhino directly from your editor.

If you're choosing between assistants and aren't sure, start with [Claude Desktop](../connector); it's the gentler entry point.

## Before you start

1. The **ai** plugin is installed in Rhino. See [Getting Started](../) if you haven't done that yet.
2. **VS Code 1.99** or newer, with the **GitHub Copilot** and **GitHub Copilot Chat** extensions installed and signed in.
3. Agent mode is available in Copilot Chat. Open the Chat view and switch the mode selector from **Ask** to **Agent**.

## Wire up the Rhino server

1. In Rhino, run the `MCPConnect` command. It prints the command Copilot needs to launch the Rhino MCP router.
2. In your workspace, create `.vscode/mcp.json` (or edit your User `settings.json` if you want it available everywhere).
3. Add an entry for the Rhino server, pasting the command and args from step 1:

   ```json
   {
     "servers": {
       "rhino": {
         "command": "rhino-mcp-router",
         "args": ["--default-version", "8"]
       }
     }
   }
   ```

4. Reload VS Code. In the Copilot Chat agent-mode tool picker, you should see the `rhino` server and its tools listed.

> **Pick the Rhino version** by changing the `--default-version` arg.
> Use `8` for Rhino 8, `9` for Rhino 9 WIP.

## Try it out

Open Copilot Chat in **Agent** mode and follow the prompts on the [Try it out](../../try-it-out) page.
