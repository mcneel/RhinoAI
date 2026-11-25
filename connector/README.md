# Claude Desktop connector

An [MCP Bundle](https://github.com/anthropics/mcpb) (`.mcpb`) that installs RhinoAI into Claude Desktop. It contains no server logic: `router-launcher.mjs` finds the router inside the user's installed Rhino-MCP-Platform package and runs it over stdio.

The plug-in must be installed from the Rhino Package Manager first.

## Files

| File | Purpose |
|---|---|
| `manifest.json` | Bundle manifest: entry point, user config (Rhino version, startup timeout, custom Rhino paths), metadata |
| `build.mjs` | Packs `connector.mcpb` from a staging copy, injecting `../shared/router-launcher.mjs` |
| `icon-*.png`, `screenshots/` | Store assets (Git LFS) |

## Build and install

In VS Code, run the **install connector** task (Terminal → Run Task…). It packs `connector.mcpb` and opens it so Claude Desktop installs it. Use **pack connector** to build without installing.

- If the connector is already installed, uninstall it in Claude Desktop first, then run the task again.

From a shell:

```sh
npx @anthropic-ai/mcpb validate manifest.json
node build.mjs                     # writes connector.mcpb
npx @anthropic-ai/mcpb info connector.mcpb
```

Don't run `mcpb pack` directly. Nothing sits at `connector/router-launcher.mjs` in the working tree, so packing the folder as-is produces a bundle with no launcher. `build.mjs` packs from a staging copy and writes the launcher into it from `shared/`. For the same reason, don't commit a `router-launcher.mjs` here.

Reference: [Building MCP bundles](https://claude.com/docs/connectors/building/mcpb).

## User config → router

| Manifest field | Passed to router as |
|---|---|
| `rhino_version` | `--default-version` |
| `startup_timeout` | `--startup-timeout` |
| `rhino_8_location` | `RHINO_MCP_RHINO_EXE_8` |
| `rhino_9_location` | `RHINO_MCP_RHINO_EXE_9`, `RHINO_MCP_RHINO_EXE_WIP` |

Bump `version` in `manifest.json` when the bundle changes. Releases are built by `.github/workflows/connector-release.yml`.

## Privacy

The connector runs entirely on the user's machine and does not collect, log, or send data on its own. Tool results go back to Claude through the MCP channel and are handled under Anthropic's privacy policy. Some tools (`run_command`, `run_python`, `run_csharp`, `open_doc`, `save_doc`) can run code or touch any file the user's account can access.

Full policy: [PRIVACY.md](../PRIVACY.md). The manifest's `privacy_policies` field must keep pointing at it.
