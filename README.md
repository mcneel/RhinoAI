<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="art/logo-dark.svg">
  <img alt="Rhino MCP" src="art/logo.svg" width="180">
</picture>

# RhinoAI

**Official Open-Source AI integration for Rhino 3D**

</div>

---

An MCP server, agent SDK, and in-app AI panel for [Rhino 3D](https://www.rhino3d.com) and Grasshopper.

RhinoAI exposes a live Rhino document to any Model Context Protocol client (Claude, Codex, Gemini, Copilot, LM Studio, and others) through a small set of typed tools: inspect objects, run commands, execute C# or Python, drive Grasshopper 1 and 2 canvases, capture viewports. It also hosts agents inside Rhino through the Agent Client Protocol (ACP).

MIT licensed. Contributions welcome.

```
 MCP client ──stdio──▶ rhino-mcp-router ──HTTP──▶ RhinoAI.rhp (inside Rhino)
 (Claude, Codex…)       spawns / tracks Rhinos       tools, resources, panel, agents
```

## Just want to use it?

You don't need to build anything. The fastest path is [Claude Desktop](https://mcneel.github.io/RhinoAI/docs/getting-started/connector/): install the Rhino connector `.mcpb` from the [latest connector release](https://github.com/mcneel/RhinoAI/releases) and it installs the Rhino plug-in for you.

User guides live at [Rhino AI Developer Docs](https://developer.rhino3d.com/guides/ai/).

The rest of this file is for working on RhinoAI itself.

## Repository layout

| Path | What it is | Language |
|---|---|---|
| [`rhino/plugin`](rhino/plugin) | The Rhino plug-in (`RhinoAI.rhp`). Hosts the MCP HTTP endpoint, tools, resources, commands, and the AI panel. | C# (net8.0, net48 on Windows) |
| [`rhino/router`](rhino/router) | `rhino-mcp-router`, a stdio MCP server that launches Rhino instances and proxies tool calls to them. | C# (NativeAOT / single-file) |
| [`rhino/sdk`](rhino/sdk) | `RhinoAI.Sdk`: agents, harnesses, model adapters, MCP clients, permissions. | C# |
| [`rhino/acp`](rhino/acp) | Agent Client Protocol implementation with generated types. | C# |
| [`rhino/panel`](rhino/panel) | The AI panel UI as a webview-hosted TypeScript app. Zero runtime dependencies. | TypeScript |
| [`connector`](connector) | Claude Desktop extension (`.mcpb`) that wraps the router. | Node |
| [`cc-plugin`](cc-plugin) | Claude Code plugin: agents, commands, and skills for Rhino. | Markdown / Node |
| [`shared`](shared) | `router-launcher.mjs`, which finds the router inside an installed package. | Node |
| [`tests`](tests) | Unit, integration, and launcher tests. | C# / Node |
| [`docs`](docs) | User documentation site, published to [mcneel.github.io/RhinoAI](https://mcneel.github.io/RhinoAI). | Hugo |

## Requirements

- [.NET SDK 8.0](https://dotnet.microsoft.com/download) (10.0 also needed for `tests/sdk.tests`)
- Rhino 8 or Rhino 9 (WIP/Beta) on Windows or macOS (Apple Silicon) to run the plug-in
- Node 24 for the panel, connector, and launcher
- Git LFS (binary assets)
- Hugo extended 0.162+ only if you work on `docs/`

You can build and unit-test the router, server, and SDK without Rhino installed.

## Quick start

```sh
git clone https://github.com/mcneel/RhinoAI
cd RhinoAI
git lfs pull

# Build and test the parts that don't need Rhino
dotnet build rhino/router/Router.csproj -c Release
dotnet test tests/Router.Tests -c Release
dotnet test tests/Server.Tests -c Release

# Build the plug-in for Rhino 8 or Rhino 9
dotnet build rhino/plugin/RhinoAI.csproj -c Debug -p:RhinoTarget=R8
dotnet build rhino/plugin/RhinoAI.csproj -c Debug -p:RhinoTarget=R9
```

Output lands in `rhino/plugin/bin/<RhinoTarget>-<os>/<Configuration>/`, including the bundled router under `router/<rid>/`.

To load your build, point Rhino at the output folder and launch it:

```sh
# Windows
set RHINO_PACKAGE_DIRS=<repo>\rhino\plugin\bin\R8-win\Debug
"C:\Program Files\Rhino 8\System\Rhino.exe" /netcore /nosplash
```

### VS Code

Open the repo in VS Code and use **Run and Debug**. The launch configs in `.vscode/launch.json` build the plug-in, launch Rhino with your build loaded, and start the MCP server in one step. `.vscode/tasks.json` also has tasks to build R8/R9, build the router, pack and install yak packages, register the router with Claude, and pack or install the connector.

In Visual Studio or Rider, the `launchSettings.json` profiles in `rhino/plugin/Properties` do the same. In Rhino, run `MCPStatus` to confirm the server is listening.

See [`rhino/plugin/README.md`](rhino/plugin/README.md) for build properties and connecting a client to a dev build.

## Branches

| Branch | Purpose |
|---|---|
| `main` | Default development branch. |
| `rhino-9.x` | Release branch. Pushes here tag `plugin-v<version>` and publish R8 packages to the Rhino Package Manager. |
| `<name>/<topic>` | Feature branches, e.g. `callum/9.x/rhino-locator`. |

The plug-in version lives in one place: `RhinoAIPackageVersion` in [`rhino/Directory.Build.props`](rhino/Directory.Build.props).

## CI

| Workflow | Runs on | Covers |
|---|---|---|
| `build.yml` | Windows, macOS | Router + Server tests, plug-in build for R8/R9, yak packaging, release |
| `sdk.yml` | Ubuntu | SDK build and tests |
| `launcher.yml` | Windows, macOS | `router-launcher.mjs` tests against a published router |
| `connector.yml` | Ubuntu | `.mcpb` manifest validation and pack |
| `docs.yml` | Ubuntu | Hugo site build and GitHub Pages deploy |

Release builds treat warnings as errors.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a PR. The short version:

- Open an issue or forum thread first for anything larger than a bug fix.
- Keep PRs scoped to one component when possible.
- Add or update tests next to your change.
- Files drafted by an AI coding agent use the `.ai.cs` suffix. See [AGENTS.md](AGENTS.md).

## Community and support

- Questions, discussions, and ideas: [discourse.mcneel.com › Rhino › Artificial Intelligence](https://discourse.mcneel.com/c/rhino/artificial-intelligence-rhino/162)
- Bugs and feature requests: [GitHub Issues](https://github.com/mcneel/RhinoAI/issues)
- User docs: [mcneel.github.io/RhinoAI](https://mcneel.github.io/RhinoAI)

## License

[MIT](LICENSE) © Robert McNeel & Associates. See also [PRIVACY.md](PRIVACY.md) and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
