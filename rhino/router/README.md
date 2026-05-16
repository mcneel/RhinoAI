# Router
The Router offers a much more powerful agentic experience when using MCP servers with Rhino3D and Grasshopper 1/2. The Router is a io based MCP server that routes any MCP requests through to Rhino.

## Advantages

Having an IO based MCP Server outside of Rhino offers many distinct advantages
1. IO can connect immediately, no need to restart Claude or Sync Rhino with it
1. As the router is outside of Rhino, it can start Rhino for the user
1. And as a bonus, it can run multiple Rhino instances at once
1. The Router can handle Crash Recovery, if Rhino crashes the Agent is informed of this, and crucially, why so it can work around the crash and recover completely.
1. The Router can start Rhino 8 and/or 9 instances at the same time, so upgrading or downgrading can be handled

## Concurrency

As the router is IO based, when an AI agent starts it will create a new instance of the router, which means multiple AI agents would each have their own instance with no shared state. To prevent issues with this, the router has no state inside of it, and stores all current state in a simple sqlite database. Meaning that multiple AI agents can each open their own rhinos and interact with them simultaneously.

## Development notes

- The Router has 0 rhino dependencies to keep it small.
- It uses codegen to generate generic tool wrappers for itself from the rhino plugin source.
- Rhino 8/9 get a slightly different router as 8 cannot access the latest G2 due to nuget.
- The Router is published as NativeAOT on mac and a simple .exe on Win to minimize size and dependencies
- 
