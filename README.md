<div align="center">

<img src="art/logo.svg" alt="Rhino MCP" width="180" />

# Rhino MCP Platform

**A Rhino MCP Server for AI Agents to create and edit Rhino.**

</div>

---

# Getting Started

## Building & Debugging

Use Run and Debug from within VSCode to build, launch Rhino and start the MCP Server all in one click.

## Using

1. Open up your AI Agent, in this case we'll use Claude.
2. Run `mcp add --transport http rhino http://localhost:4862` (Or whichever Port you are running on, confirm with the command RhinoMCP)
3. Claude will add the MCP Server
4. You may need to restart Claude
5. Confirm everything is working by asking Claude to create a box in Rhino

https://github.com/user-attachments/assets/9b1cd938-3995-4eec-ab42-d62bf67b13f2

## Issues?

Q: Claude cannot find the MCP Server

A: Ensure you've run the `RhinoMCP` command, if that still fails, try a different port.

### Configuration

RhinoMCP has one command, aptly named `RhinoMCP` which will start the server and let you change the port it is using.

# Getting Help

Ask questions, post discussions and ideas to the [Rhino Discourse forums](https://discourse.mcneel.com/c/rhino/artificial-intelligence-rhino/162).

# What can an MCP Server do?

MCP Servers are a new way of controlling your programs. They let you control Rhino but using written human language. You can ask about the model, have your AI agent create things in the model, or organise it for you. The capabilities are endless.
