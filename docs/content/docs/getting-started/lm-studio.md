---
title: Local LLMs
icon: lmstudio
weight: 7
prev: docs/getting-started
next: docs/try-it-out
toc: false
author: Callum
editor: SteveF
keywords:
  - LM Studio
  - local LLM
  - open-weight
  - self-hosted
---

![Local LLM](/local-llm.png)

[LM Studio](https://lmstudio.ai) is a desktop app for running open-weight models on your own machine. It speaks MCP, so once you point it at the Rhino server a local model can drive Rhino the same way a hosted one can.

If you're choosing between assistants and aren't sure, start with [Claude Desktop](../connector); it's the gentler entry point. Pick LM Studio when you'd rather keep everything on your own hardware or have data and privacy concerns.

{{< callout type="warning" >}}
Local open-weight models are not as capable as the paid hosted models (Claude, GPT, etc.). Expect more retries, weaker reasoning on long chains of tool calls, and the occasional refusal to use tools at all.
{{< /callout >}}

## 1. Install LM Studio

1. Install [LM Studio](https://lmstudio.ai), then download a model with strong tool-use support. A good starting point is [Qwen3](https://lmstudio.ai/models/qwen3), which drives MCP tools reliably. Plan on **16 GB of RAM** as a minimum.
2. **Crank up the context length.** LM Studio defaults to a small context window, which the Rhino tool list alone will blow through. Open the model's load settings and push the max context length as high as your machine will allow.

## 2. Install the Rhino plugin

{{< yak package="Rhino-MCP-Platform" version="8" >}}
{{< yak package="Rhino-MCP-Platform" version="9" >}}

If that doesn't work you can try the below:

1. Open Rhino 8 (and/or Rhino 9 WIP)
2. Run the `PackageManager` command
3. Search for, and install Rhino-MCP-Platform

## 3. Wire up the Rhino server

1. In Rhino, run the `MCPConnect` command, choose the mcp.json tab. It gives the JSON needed for LM Studio to connect to the Rhino MCP.
2. In LM Studio, open the **Program** sidebar and click **Install &gt; Edit mcp.json** (or open `~/.lmstudio/mcp.json` directly).
3. Add an entry for the Rhino server, pasting the mcp.json tab.
4. Save the file. LM Studio picks up the change without a restart; the `rhino` server should appear in the MCP tool list for any new chat.

> **Pick the Rhino version** by changing the `--default-version` arg.
> Use `8` for Rhino 8, `9` for Rhino 9 WIP/BETA.

## Try it out

<blockquote class="page-note">
Load your model in LM Studio, start a new chat with tool use enabled, and follow the prompts on the <a href="../../try-it-out">Try It Out</a> page.
</blockquote>

## Tips

- **Tool use must be on.** In the chat sidebar, make sure the `rhino` server is toggled on for the session. LM Studio defaults to asking before each tool call.
- **Smaller models struggle.** If the model keeps describing what it would do instead of calling tools, try a larger or more recent instruction-tuned model.
