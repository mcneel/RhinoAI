---
title: Developers
linkTitle: Developers
weight: 2
---

These pages are about incorporating the Rhino MCP and an AI assistant, like Claude Code into your development cycle for rapid development.

These pages are **not** tutorials on how to do the conversion by hand. The goal is to put an assistant in the driver's seat and review what it does.

{{< cards >}}
  {{< card link="upgrade-to-netcore" title="Upgrade a plugin to .NET Core" subtitle="Move a `net48` plugin onto `net7.0` / `net8.0` using Claude Code + Rhino MCP." >}}
  {{< card link="upgrade-rhino-version" title="Upgrade to a newer Rhino" subtitle="Move a plugin from Rhino 7 → 8 or 8 → 9 with the assistant driving." >}}
  {{< card link="upgrade-gh1-to-gh2" title="Upgrade GH1 components to GH2" subtitle="Scaffold GH2 equivalents and verify them side-by-side on a GH1 + GH2 canvas." >}}
  {{< card link="plugin-prototyping" title="Prototype a plugin" subtitle="\"Make me a plugin that does X.\" Scaffold, build, load, exercise &mdash; all through the MCP loop." >}}
{{< /cards >}}

If you're here to hack on **Rhino MCP itself**, see the
[contributing notes on GitHub](https://github.com/mcneel/RhinoMCP#building--debugging).
