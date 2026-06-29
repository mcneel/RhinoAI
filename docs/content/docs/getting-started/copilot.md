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

## 1. Install GitHub Copilot

[VS Code 1.99](https://code.visualstudio.com) or newer, with the **GitHub Copilot** and **GitHub Copilot Chat** extensions installed and signed in. Agent mode lives in Copilot Chat: open the Chat view and switch the mode selector from **Ask** to **Agent**.

## 2. Install the Rhino plugin

{{< yak package="Rhino-MCP-Platform" version="8" >}}
{{< yak package="Rhino-MCP-Platform" version="9" >}}

If that doesn't work you can try the below:

1. Open Rhino 8 (and/or Rhino 9 WIP)
2. Run the `PackageManager` command
3. Search for, and install Rhino-MCP-Platform

## 3. Wire up the Rhino server

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
> Use `8` for Rhino 8, `9` for Rhino 9 WIP/BETA.

## Try it out

<blockquote class="page-note">
Open Copilot Chat in <strong>Agent</strong> mode and follow the prompts on the <a href="../../try-it-out">Try It Out</a> page.
</blockquote>
