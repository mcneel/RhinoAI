---
title: Claude Desktop
icon: claude
weight: 2
prev: docs/getting-started
next: docs/try-it-out
toc: false
author: Callum
editor: SteveF
keywords:
  - Claude Desktop
  - connector
  - Anthropic
  - setup
---

[Claude Desktop](https://claude.ai/download) is Anthropic's user-friendly chat app for Windows and Mac. With our connector installed, anything you ask Claude in the chat window can now happen in Rhino & Grasshopper.

## 1. Install Claude Desktop

[Claude Desktop](https://claude.ai/download)

## 2. Install the Claude connector

1. Download [`connector.mcpb`](https://github.com/mcneel/RhinoMCP/releases/download/connector-v0.1.3/connector.mcpb).
2. Open Claude Desktop and go to `Settings` &rarr; `Extensions` &rarr; `Advanced settings`.
3. Click `Install Extension` and select the downloaded `connector.mcpb`.
4. Click `Install` to confirm.

That's it. The connector is now wired up.

## 3. Install the Rhino plugin

{{< yak package="Rhino-MCP-Platform" version="8" >}}
{{< yak package="Rhino-MCP-Platform" version="9" >}}

If that doesn't work you can try the below:

## 3.b Install the Rhino plugin (with Claude)

> The desktop connector can also install the plugin for you if you ask it to.

Paste the following into your AI agent. It will install it for you, so there is no need to open Rhino.

{{< prompt >}}
Install the `Rhino-MCP-Platform` plugin into Rhino using Yak (Rhino's Package Manager). `$1` is the Rhino major version to target (e.g. `8`). If omitted, default to `8`.

1. Locate the Yak CLI for Rhino `$1`. It ships with Rhino:
   - macOS: `/Applications/Rhino $1.app/Contents/Resources/bin/yak`
   - Windows: `C:\Program Files\Rhino $1\System\Yak.exe`
2. Install the plugin from the public package server:
   
   `yak install Rhino-MCP-Platform`
   
3. Restart Rhino `$1` so the newly installed plugin is loaded.
4. In Rhino, run the `MCPStart` command to confirm the plugin is available.

To upgrade an existing install, run `yak update Rhino-MCP-Platform`. To remove it, run `yak uninstall Rhino-MCP-Platform`.
{{</ prompt >}}

## Try it out

<blockquote class="page-note">
Open Claude Desktop, start a new chat, and follow the prompts on the <a href="../../try-it-out">Try It Out</a> page.
</blockquote>
