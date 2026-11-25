# RhinoAI plug-in

`RhinoAI.rhp`, the Rhino plug-in at the center of the project. Published to the Rhino Package Manager as **Rhino-MCP-Platform**.

It does four things:

1. Runs an MCP server over HTTP on `localhost` inside Rhino (default port `10500`; router-launched Rhinos use the next free ports).
2. Registers the tools and resources that MCP clients call.
3. Hosts the AI panel and in-Rhino agents (Claude, Codex, Gemini via ACP).
4. Bundles the [router](../router) binary so external clients have a stable stdio entry point.

## Layout

| Path | Contents |
|---|---|
| `Server/` | MCP endpoint, JSON-RPC protocol, tool/resource registries, schema builder, parameter binding |
| `Tools/` | Rhino tools (`run_command`, `list_objects`, `get_viewport_image`, …) |
| `Tools/GH1`, `Tools/GH2` | Grasshopper 1 (`g1_*`) and Grasshopper 2 (`g2_*`) canvas tools |
| `src/Resources/` | MCP resources: installed plug-ins, command help, host environment |
| `Agents/` | In-Rhino agent hosting: ACP client, CLI processes, conversation store, stream-JSON parsers |
| `Commands/` | Rhino commands: `MCPStart`, `MCPStatus`, `MCPConnect`, `AI`, `AIPanel`, `Claude`, `Codex`, plus the hidden `MCPSpawn` the router uses to autostart a child Rhino |
| `UI/` | Eto panel, settings pages, and the bridge to the [TypeScript panel](../panel) |
| `RhinoCode/`, `ScriptProjects/` | C# and Python execution via RhinoCode |

## Building

```sh
dotnet build RhinoAI.csproj -c Debug -p:RhinoTarget=R8   # Rhino 8
dotnet build RhinoAI.csproj -c Debug -p:RhinoTarget=R9   # Rhino 9 (default)
```

### Build properties

| Property | Default | Notes |
|---|---|---|
| `RhinoTarget` | `R9` | `R8` or `R9`. Selects RhinoCommon/Grasshopper package versions and defines the matching constant. The SDK reference is R9-only. |
| `PluginOS` | host OS | `win`, `osx`, or `linux` |
| `RouterRids` | `win-x64;win-arm64` / `osx-arm64` | Router runtimes to publish into `router/`. Linux has no default; pass `linux-x64` or `linux-arm64` explicitly. |
| `IsInRhino` | auto | `true` only when building inside the Rhino source tree. External builds are always `false`. |

On Windows the plug-in builds for both `net8.0` and `net48`. On macOS and Linux it builds `net8.0` only, with a flat output folder.

Output: `bin/<RhinoTarget>-<PluginOS>/<Configuration>/`.

## Running a dev build

In VS Code, pick a configuration in **Run and Debug** (`.vscode/launch.json`): it builds, launches Rhino with your build loaded, and starts the MCP server.

Manually: set `RHINO_PACKAGE_DIRS` to the output folder and start Rhino with `/netcore`. The profiles in `Properties/launchSettings.json` do this for Rhino 9 Beta and a Rhino source build; add one for your Rhino 8 path if needed.

In Rhino:

- `MCPStatus` shows the port and connection state.
- `MCPStart` starts the server if it isn't running.
- `MCPConnect` opens the connect dialog: install configs for detected MCP clients, or copy the JSON config and env overrides.

To point an external client at your dev build instead of the installed package, run that client against the router in your output folder: `bin/<target>-<os>/Debug/router/<rid>/rhino-mcp-router`.

## Adding a tool

```csharp
[McpServerToolType]
internal static class ZoomToLayerTool
{
    [McpServerTool("zoom_to_layer", "Zoom To Layer", readOnly: false, destructive: false)]
    [Description("Zoom the active viewport to fit all objects on a layer (full path).")]
    public static IToolResult ZoomToLayer(
        RhinoDoc doc,
        [Description("Layer full path")] string layer)
    {
        if (doc.IsHeadless)
            return Failure(ToolError.RH_Doc_Headless);
        // ...
    }
}
```

- Tools are discovered by attribute. No registration list to edit.
- The router picks up new tools at build time: a source generator in `rhino/router/codegen` reads the plug-in's tool files and emits a proxy that adds a `slot` argument. Primitive parameters pass through; other types become open JSON objects.
- The JSON schema is generated from the method signature and `[Description]` attributes.
- `RhinoDoc` is injected; it is not part of the schema.
- Return `Failure(...)` for expected errors so the client gets a readable result instead of a transport error.
- `[BackgroundThread]` and `[InPanelOnly]` change where and when a tool runs.
- Tools prefixed `_router_` are internal to the router contract. Don't call or rename them from clients.

Tests go in [`tests/Server.Tests`](../../tests/Server.Tests).
