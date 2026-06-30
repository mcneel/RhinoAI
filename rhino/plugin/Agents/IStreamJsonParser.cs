using System.Collections.Generic;
using System.Diagnostics;
using Acp;

namespace RhMcp;

// A per-CLI strategy that the runner composes. It owns ONLY the CLI-specific knowledge: how to
// launch, how to frame an outgoing turn, and how to translate one stdout line into ACP
// session/update events. It owns NO process, NO threads, NO turn gating, and no AISettings/RhinoApp
// access (the runner reads settings and resolves the MCP server set). RhinoApp-free so it can be
// Compile Include'd into a test project (mirrors Server.Tests). Pure + deterministic.
//
// Earned by two real impls: ClaudeStreamJsonParser, CodexStreamJsonParser.
internal interface IStreamJsonParser
{
    // The human label used only in [name:...] log lines and not-found messages.
    public string DisplayName { get; }

    // The message thrown when the CLI binary is not found at any SearchPath.
    public string NotFoundMessage { get; }

    // Append all launch args for one process spawn. 'resume' is false on the first spawn (open the
    // session fresh, e.g. --session-id <id>) and true on a respawn after cancel/crash (continue it,
    // e.g. --resume <id>). mcpUrl is this doc's HTTP listener. agentSessionId is the stable
    // continuity token. mcpServers is the resolved MCP server set the runner read from AISettings.
    // The parser owns the resume-vs-fresh flag names so the session/resume contract is one CLI's
    // concern, not the runner's.
    public void ConfigureArguments(ProcessStartInfo psi, string mcpUrl, string agentSessionId, IReadOnlyList<string> mcpServers, bool resume);

    // The single newline-framed stdin line that carries one user turn (a JSON envelope for Claude,
    // plain text for Codex). Pure: built only from the ACP prompt blocks the runner already has.
    public string FormatTurn(IReadOnlyList<Acp.ContentBlock> prompt);

    // Translate one stdout line into zero or more ACP events for THIS turn, plus whether the turn is
    // now complete. Pure function of (line) -> events: the runner is what pushes the SessionUpdates
    // to RhinoAcpClient and resolves the turn TCS on completion. The parser never touches the client
    // or the process.
    public ParsedLine Parse(string line);
}
