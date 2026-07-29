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

`McpExtensionHost` also offers `UnregisterMcpTool`, `UnregisterMcpToolsByOwner`,
`RegisteredMcpToolNames`, and `RegisterMcpResultTransform` — a hook that can rewrite the
result of *any* tool call this server serves, including tools compiled into this plug-in.

### Three rules that are not style preferences

1. **Only strings and corelib delegates cross the boundary.** This plug-in is built with
   `EnableDynamicLoading=true`, so it loads into its own `AssemblyLoadContext` with its
   dependencies copied beside the `.rhp`. A caller in the default context binds a
   *different* `System.Text.Json`, so a `JsonElement` passed across would be a different
   type with the same name — the cast fails silently and the tool simply never appears.
   `string`, `Func<>`, `Task<>` and `CancellationToken` live in System.Private.CoreLib,
   which can never be loaded twice, so they are the same type everywhere.
2. **Do not register from `OnLoad`.** `RhinoApp.GetPlugInObject` loads the target plug-in,
   and reaching for another plug-in during your own load is a reentrant plug-in load
   inside Rhino's plug-in manager. Defer to idle. Load order then stops mattering: this
   plug-in is loaded on demand by the call, and registering before the MCP server starts
   is fine because the dispatcher reads the registry live.
3. **Contributed tools run on a background thread by default** — the inverse of the
   default for tools compiled into this plug-in. A contributing plug-in does its own
   UI-thread marshalling and knows what it touches, and its work may run for minutes;
   holding the Rhino message pump for that would freeze the application. Set
   `"requiresUiThread": true` to opt in, which covers only the synchronous prefix of an
   async handler.

### Reaching contributed tools through the router

The router advertises a build-time, code-generated catalog (`RouterToolGenerator` scans
`/plugin/Tools/` for `[McpServerTool]` methods), so runtime-registered tools are not in
it. A client connected straight to this plug-in's HTTP endpoint sees them as ordinary
named tools with their real schemas.
