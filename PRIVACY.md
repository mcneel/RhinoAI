# Privacy Policy

_Last updated: 2026-05-19_

This document describes how the Rhino Connector handles user data. The connector is a local desktop extension that bridges Claude to an installed copy of Rhino 3D and Grasshopper on the user's own machine.

## Data collection

The connector does not collect, log, or transmit user data on its own. It does not phone home, emit telemetry, or maintain analytics of any kind.

When Claude invokes one of the connector's tools, the connector reads or modifies state inside the running Rhino process on the user's machine (geometry, layers, viewport, Grasshopper canvases, etc.) and returns the result of that call to Claude.

## Usage, storage, and retention

All processing happens locally inside the user's Rhino process. The connector itself stores nothing between sessions. Files are written to disk only when the user (via Claude) explicitly calls a tool that does so, for example `save_doc` or `open_doc`.

Tool results are returned to Claude as MCP responses. This means any content the connector returns, geometry, command, script output or viewport screenshots, and similar, leaves the user's machine through their Claude client and is handled according to Anthropic's privacy policy, not this one.

The connector exposes tools that can execute arbitrary code or commands in the Rhino process (`run_command`, `run_python`, `run_csharp`) and tools that read or write files (`open_doc`, `save_doc`). These tools can touch any file the user's account has access to. Users should treat the connector with the same caution they would any tool that runs scripts on their machine.

## Third-party sharing

The connector does not share data with third parties. The only outbound flow is the MCP response channel back to Claude, described above.

## Contact

Questions or concerns about this policy:

- Email: support@mcneel.com
- Support forum: https://discourse.mcneel.com/c/rhino/artificial-intelligence-rhino/162
- Issues: https://github.com/mcneel/ai/issues
