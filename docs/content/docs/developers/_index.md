---
title: Developers
linkTitle: Developers
icon: code
weight: 30
prev: docs
next: docs/developers/upgrade-to-netcore
author: Callum
editor: SteveF
keywords:
  - developers
  - plugin development
  - Claude Code
  - AI assistant
---

These pages are about incorporating the Rhino MCP and an AI assistant like Claude Code into your development cycle for rapid development.

These pages are **not** tutorials on how to do the conversion by hand. The goal is to put an assistant in the driver's seat and review what it does.

{{< cards >}}
  
  {{< card link="upgrade-to-netcore" title="Upgrade a plugin to .NET Core" subtitle="Move a `net48` plugin onto `net7.0` / `net8.0` using Claude Code + Rhino MCP." >}}
  
  {{< card link="upgrade-gh1-to-gh2" title="Upgrade GH1 components to GH2" subtitle="Scaffold GH2 equivalents and verify them side-by-side on a GH1 + GH2 canvas." >}}
  
  <!-- draft
  {{< card link="plugin-prototyping" title="Prototype a plugin" subtitle="\"Make me a plugin that does X.\" Scaffold, build, load, exercise, all through the MCP loop." >}}
  
  {{< card link="windows-to-cross-platform" title="Make a Windows plugin cross-platform" subtitle="Replace P/Invokes, registry calls, and other Windows-isms so the plugin runs on Mac." >}}
  
  {{< card link="wpf-to-eto" title="Convert a WPF UI to Eto" subtitle="Port a Windows-only WPF UI to Eto.Forms so the plugin runs on Mac too." >}}
  -->

{{< /cards >}}

<blockquote class="page-note">
If you're here to hack on <strong>Rhino MCP itself</strong>, see the <a href="https://github.com/mcneel/ai#building--debugging">contributing notes on GitHub</a>.
</blockquote>
