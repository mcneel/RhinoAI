# Claude Desktop Connector

## Build and install

Run the VS Code task **build and install mcpb** (Terminal → Run Task…). It packs
`connector.mcpb` and opens it so Claude Desktop installs it.

- If you have already installed the connector, uninstall it in Claude Desktop first,
  then run the task again.
- Use the **pack mcpb** task if you only want to build the bundle without installing.

Both tasks run `node build.mjs`, which packs from a staging copy and writes `router-launcher.mjs` into it straight from `../shared/router-launcher.mjs`. Don't run `mcpb pack` directly: nothing sits at `connector/router-launcher.mjs` in the working tree, so packing the folder as-is produces a bundle with no launcher in it.

## Reading

- https://claude.com/docs/connectors/building/mcpb

## Privacy Policy

The connector runs entirely on the user's machine and does not collect, log, or transmit data on its own. Tool results are returned to Claude through the MCP channel and handled under Anthropic's privacy policy. Some tools (`run_command`, `run_python`, `run_csharp`, `open_doc`, `save_doc`) can execute code or touch files the user's account has access to.

Full policy: https://github.com/mcneel/RhinoMCP/blob/main/PRIVACY.md
