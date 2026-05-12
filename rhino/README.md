# Rhino MCP Server Plugin

The Rhino MCP Server works in a unique way. The MCP Link and all requests are sent to the router app. 

The router app communicates to AI Agents via IO rather than HTTP. This has several benefits
- More stable connection
- No socket interference
- The MCP Server picks it up immediately, no reconnect needed
- The router can launch as many Rhino instances for us as we need
