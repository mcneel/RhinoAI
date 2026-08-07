# Rhino MCP Server Plugin

The Rhino MCP Server works in a unique way. The MCP Link and all requests are sent to the router app. 

The router app communicates to AI Agents via IO rather than HTTP. This has several benefits
- More stable connection
- No socket interference
- The MCP Server picks it up immediately, no reconnect needed
- The router can launch as many Rhino instances for us as we need

## Extending the tool set from another Rhino plug-in

Any Rhino plug-in can contribute MCP tools to this server at run time. There is no
assembly reference in either direction and nothing to build against: the plug-in fetches
this one's registration object and calls it by reflection.

```csharp
// In your plug-in, from a one-shot RhinoApp.Idle handler -- NOT from OnLoad.
object host = RhinoApp.GetPlugInObject(new Guid("2668d7ed-f507-4a68-8295-8172147a0e39"));
if (host == null) return;   // Rhino MCP is not installed; carry on without it.

MethodInfo register = host.GetType().GetMethod(
    "RegisterMcpTool",
    BindingFlags.Public | BindingFlags.Instance, null,
    new[] { typeof(string), typeof(Func<string, CancellationToken, Task<string>>) }, null);

string error = (string)register.Invoke(host, new object[]
{
    descriptorJson,
    (Func<string, CancellationToken, Task<string>>)MyToolAsync,
});
// error is "" when accepted, otherwise the reason it was refused.
```

The descriptor:

```jsonc
{ "owner": "com.example.myplugin",     // required, reverse-DNS; groups your tools
  "name": "myplugin_do_thing",         // required, unique across the server
  "title": "Do Thing",                 // optional
  "description": "What it does.",      // required -- an LLM cannot use an undescribed tool
  "inputSchema": { "type": "object", "properties": { }, "required": [] },
  "annotations": { "readOnlyHint": true, "destructiveHint": false },
  "requiresUiThread": false }          // optional, default false
```

Your handler receives the call's arguments as a JSON object string and returns either an
MCP result object (`{"content":[{"type":"text","text":"…"}],"isError":false}`) or any
other string, which is wrapped as a single text block.

`McpExtensionHost` also offers `UnregisterMcpTool`, `UnregisterMcpToolsByOwner` and
`RegisteredMcpToolNames`.

### Advice

1. **Only strings and corelib delegates cross the boundary.** Pass JSON as a `string`,
   never a `JsonElement` — this plug-in lives in its own `AssemblyLoadContext`, so any
   other type fails silently and the tool never appears.
2. **Register from a one-shot `RhinoApp.Idle` handler, not `OnLoad`.** Load order then
   does not matter in either direction.
3. **Handlers run on a background thread by default.** Do your own UI-thread marshalling.
   `"requiresUiThread": true` opts in, but covers only the synchronous prefix of an async
   handler.

### Through the router

Contributed tools are listed and callable through the router with their real schemas, the
same as on a direct HTTP connection. What to expect:

- A tool registered mid-session surfaces within about fifteen seconds — or immediately on
  the client's next `tools/list` — and the router emits
  `notifications/tools/list_changed` when the set changes.
- A name that collides with a tool the router already serves, or begins with `_`, is
  withheld from the merged list.
- Calls go to the Rhino that contributed the tool. The router never launches a Rhino to
  list or to serve a contributed call.
- An unreachable or misbehaving Rhino contributes nothing rather than breaking the tool
  list; the `Contributed tools: N from M slot(s)` stderr log line says what happened.
