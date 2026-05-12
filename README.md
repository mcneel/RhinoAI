<div align="center">

<img src="art/logo.svg" alt="Rhino MCP" width="180" />

# Rhino MCP Platform

**A Rhino MCP Server for AI Agents to create and edit Rhino.**

</div>

---

# Getting Started

## Installing

Rhino MCP Platform can be installed without building source code.

1. Run `PackageManager` in Rhino
2. Search Rhino-MCP-Platform
3. Install
4. Skip to [Using](https://github.com/mcneel/RhinoMCP#using)

## Building & Debugging

Use Run and Debug from within VSCode to build, launch Rhino and start the MCP Server all in one click.

## Using

1. Open up your AI Agent, in this case we'll use Claude.
2. Run the RhinoMCPConnect command and copy that into your AI Agent
3. You may need to restart your AI session
4. Confirm by asking your agent to create a box in Rhino.

https://github.com/user-attachments/assets/9b1cd938-3995-4eec-ab42-d62bf67b13f2

### Options

The router accepts `--default-version <ver>` (or `-v <ver>`) to pick which installed Rhino to launch. Defaults to `8`; pass `9` for Rhino 9 WIP.

## Issues?

Q: My MCP client can't find the router.

A: Make sure Rhino-MCP-Platform is installed via Rhino's PackageManager, and double-check the path to `rhino-mcp-router` in your MCP client config.

# Getting Help

Ask questions, post discussions and ideas to the [Rhino Discourse forums](https://discourse.mcneel.com/c/rhino/artificial-intelligence-rhino/162).

# What can an MCP Server do?

MCP Servers are a new way of controlling your programs. They let you control Rhino but using written human language. You can ask about the model, have your AI agent create things in the model, or organise it for you. The capabilities are endless.
