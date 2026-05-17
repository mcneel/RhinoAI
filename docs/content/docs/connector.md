---
title: Claude Desktop
weight: 2
prev: docs/getting-started
next: docs/try-it-out
toc: false
---

[Claude Desktop](https://claude.ai/download) is Anthropic's friendly chat
app for Mac and Windows. With our connector installed, anything you ask
Claude in the chat window can now happen in Rhino.

## 1. Install Claude Desktop

[Claude Desktop](https://claude.ai/download)

## 2. Install the connector

1. Download the latest [Rhino3d connector](https://claude.com/connectors/rhino3d) from the Claude connectors page.
2. Click `Claude` under "Used in".

That's it. The connector is now wired up.

## 3. Install the Rhino plugin

> The desktop connector can also install the plugin for you if you ask it to.

Paste the following into your AI agent, it will install it for you, there is no need to open Rhino.

{{< prompt >}}
Install the `Rhino-MCP-Platform` plugin into Rhino using Yak (Rhino's package manager). `$1` is the Rhino major version to target (e.g. `8`). If omitted, default to `8`.

1. Locate the Yak CLI for Rhino `$1`. It ships with Rhino:
   - macOS: `/Applications/Rhino $1.app/Contents/Resources/bin/yak`
   - Windows: `C:\Program Files\Rhino $1\System\Yak.exe`
2. Install the plugin from the public package server:
   
   yak install Rhino-MCP-Platform
   
3. Restart Rhino `$1` so the newly installed plugin is loaded.
4. In Rhino, run the `MCPStart` command to confirm the plugin is available.

To upgrade an existing install, run `yak update Rhino-MCP-Platform`. To remove it, run `yak uninstall Rhino-MCP-Platform`.
{{</ prompt >}}

## Try it out

Open Claude Desktop, start a new chat, and follow the prompts on the
[Try it out](../try-it-out) page.
