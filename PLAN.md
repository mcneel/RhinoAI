# rh-mcp — Plan (converge, harden, ship)

## Status

The in-Rhino agent skeleton is now a working product on `callum/acp`: the AI Panel (bubbles + TranscriptViewModel), the 3-tab AISettingsDialog, non-blocking dual-channel `ask_user`, conversation history, plus an ACP protocol library (`rhino/acp/`) and a native-ACP Gemini path. The prior build plan (Parts 0-6) is delivered and lives in git history at the acp commits.

This plan is the next phase: collapse the agent layer onto a single internal contract, make onboarding zero-setup, and bet the headline on Grasshopper 2. It was agreed in a planning session on 2026-06-01.

## Product

AI augmentation for Rhino. The MCP tool core is the root; the in-Rhino AI Panel is the consumer that can't exist without it; the router and connector expose the same root to external agents. Headline bet: Grasshopper authoring on Rhino 9 / GH2. Experience bar: install Claude, auth it, just GO, no other setup.

```
  ┌─────────────────────────────┐        ┌────────────────────────────────┐
  │  In-Rhino AI Panel          │        │  External clients              │
  │  hosts agent (Claude) ⇄ ACP │        │  Claude Desktop / Code / Gemini│
  └──────────────┬──────────────┘        └───────────────┬────────────────┘
                 │ agent drives tools                     │ stdio MCP
                 │ over MCP                               ▼
                 │                                 ┌────────────┐
                 │                                 │   Router   │  stdio→HTTP proxy
                 │                                 └─────┬──────┘
                 ▼                                       │ HTTP MCP
        ┌──────────────────────────────────────────────────────────┐
        │            MCP tool core   (Rhino plugin, HTTP)           │
        │   GH2/GH1 · run_python/csharp · doc · selection · view    │
        └──────────────────────────────────────────────────────────┘
                                   │
                                   ▼
                       RhinoDoc / Grasshopper / scripting
```

## Decisions (locked with the user)

| Dimension     | Decision |
| ------------- | -------- |
| Direction     | AI augmentation for Rhino; MCP root + Panel consumer; both essential |
| Headline      | Grasshopper authoring, demo target Rhino 9 / GH2 (future bet), GH1 carried along |
| Onboarding    | Auto-detect `claude`, auto-wire MCP, auto-start, zero settings; docs link if none found |
| Tool safety   | Auto-grant all rhino tools (frictionless) + per-turn undo checkpoint as the safety net |
| Agent arch    | ACP is the single internal contract; Claude + Codex via hardened stream-json translators; Gemini native |
| Claude→ACP    | Harden our own translator (dep-free, only needs the `claude` CLI); no npm bridge |
| Codex         | Verify and fix now so Claude, Gemini, Codex all ship working |
| Code style    | Fold the C# conformance pass into the convergence work: channel-matched nulls, fail-fast, properties over fields, sealed + composition over open inheritance, async hygiene, minimal earned abstraction (per `code-style.md`) |
| UI/UX         | Incremental render + streaming polish (stop / regenerate / edit-resend); markdown + rich previews later |
| Grounding     | Pull-only, no auto-injection. Mitigated by an always-on grounding steer in `AgentPrompts` + a bundled `get_context` tool (selection + view + doc/graph in one call) |
| GH loop       | Full agentic loop: build → solve → read errors/warnings → self-correct → report |
| Discoverability | Starter prompt chips (GH2-flavored) in the empty panel |
| Result view   | Smart chip summaries (human-readable) by default; raw JSON on expand; no inline previews yet |
| Resume        | Resume/continue past conversations via `--resume` + the saved agent session id |
| Usage         | Subtle per-turn / session token indicator (cost if derivable) |
| Quality of life | Keyboard shortcuts (Esc stop, focus prompt, new convo), unread indicator, turn-complete notification |
| Testing       | Expand unit coverage (protocol, dispatch, registry, conversation, mapping); no headless harness yet |
| Persistence   | Transcripts stay in PersistentSettings (the 50-conversation cap bounds it) |
| Privacy       | Docs-only (PRIVACY.md + site), no in-product consent gate |
| Releasing     | router + plugin = one coupled version from a single source; connector + cc-plugin version independently |
| Terminology   | Glossary + targeted renames now |

## Target agent architecture (after W2)

```
        Gemini (native ACP)          Claude / Codex (stream-json CLIs)
              │                                  │
       ClientSideConnection            StreamJsonAgent (sealed; owns process mgmt,
              │                          turn gating, session/resume, read loop)
              │                                  │  composes one IStreamJsonParser
              │                          ┌───────┴───────┐
              │                       ClaudeParser    CodexParser    emit ACP session/update
              ▼                                  ▼
      ───────────────  RhinoAcpClient  (single ACP seam)  ───────────────
                                  │ Record()
                                  ▼
                          Conversation ──► AI Panel
```

ACP is the one internal contract. Gemini feeds it natively; Claude and Codex feed it through stream-json translators. The duplication dies by composition, not a base class: `CliAgent` + `ClaudeAcpAgent` + `CodexCliAgent` collapse into one sealed `StreamJsonAgent` (process management, turn gating, session/resume, read loop) that composes a per-agent `IStreamJsonParser` (Claude, Codex) which emits ACP `session/update`. The interface earns its place with two real implementations; no open behavioural inheritance, per `code-style.md`. Everything downstream sees only `RhinoAcpClient` → `Conversation`.

## Glossary (canonical terms)

| Term            | Means | Action |
| --------------- | ----- | ------ |
| Agent           | The external AI (Claude, Gemini, Codex) | Keep |
| AgentDefinition | Config / registration record for an agent | Keep (already disambiguated) |
| AgentRunner     | The live per-(doc,agent) object that owns the connection and runs turns | Rename the `IAgent` / `AcpAgent` runtime role to *Runner |
| ACP             | The wire protocol (`rhino/acp/` library) | Keep; reserve "ACP" for the protocol |
| `ACP/` folder   | The plugin's agent framework | Rename folder + namespace to `Agents/` (drop the ACP prefix) |
| Rhino process   | An OS Rhino process | Use "process"; retire "instance" |
| Listener        | An in-Rhino HTTP MCP endpoint (one process can host several) | Keep |
| Slot            | A routable endpoint the router targets | Keep (router's term) |
| Conversation    | Persisted transcript graph (one per session) | Keep |
| Turn            | One prompt + response cycle | Keep |
| AgentSessionId  | The agent process's continuity token (`--resume`) | Rename `Conversation.SessionId` to make the "agent session" meaning explicit |

## Workstreams

Ordered by dependency, not calendar. W0 (the Great Sweep) runs first to establish an all-green baseline; W1 and W2 are the foundation; the rest layer on top.

| ID | Workstream | Depends on |
| -- | ---------- | ---------- |
| W0 | Great Sweep (whole-codebase conformance) | - (runs first) |
| W1 | Glossary + targeted renames | - |
| W2 | ACP convergence + conformance + Codex verify | W1 |
| W3 | "Just GO" onboarding | W2 |
| W4 | Grasshopper authoring (R9 / GH2) | W2 (soft) |
| W5 | Streaming + UI polish | - |
| W6 | Per-turn undo checkpoint | - |
| W7 | Unit coverage | rides all |
| W8 | Version source-of-truth | - |
| W9 | Grounding support (steer + get_context) | W2 (soft) |
| W10 | Conversation resume | W2 |
| W11 | ask_user redesign (non-blocking, persistent, in-panel-only) | W5, W10 |

### W0 — Great Sweep (whole-codebase conformance, runs first)

A bespoke full-codebase audit and fix to a clean baseline before feature work. Not the session's audit harness, whose stock concept investigator targets another project (the `.rhc` / groups-containers domain); this uses our own review fleet. Scope: all C# under `rhino/` (plugin, router, acp, tests). Checks per (file, check) cell: bug, concept-validity (our domain), coding-philosophy (`code-style.md`), and dead-code. Fix policy: find, gate, then fix.

Two staged workflows with a gate between:
- Sweep-Discover: pre-screen cheaply auto-passes trivially-clean cells; investigators run per surviving (file, check) in parallel (read-only); findings are clustered and deduped by file and theme. Output is a cluster report. GATE: you approve the cluster list before anything changes.
- Sweep-Fix: per cluster, fixer ⇄ reviewer verify, build-gated (R8; R9 for GH2-touching files); clusters that share a file are serialized to avoid worktree collisions; finish with an integration build (R8 + R9) and the unit suite.

Sequencing: "sweep everything first" was chosen, so W0 precedes Stage 0. Accepted cost: W2 and W5 later rewrite parts of the agent layer and the AIPanel render, so a slice of this cleanup gets redone.

Outcome (2026-06-01): the discover pass found 64 verified clusters (6 high, 27 medium, 31 low). The fix ran in priority waves and was deliberately stopped after the substantive work: all 6 high-severity bugs and all 17 correctness mediums (every `bug` and `concept` cluster) are fixed, green (R8 plugin + router + acp build; Router/Server/Acp/Ngentic tests pass; the router now emits 38 tool proxies, was 0), and committed (`c2e96e2`, `8241634`). The remaining 41 hygiene clusters (10 philosophy/deadcode mediums incl. `var`-in-46-files, plus 31 cosmetic lows) are a deferred backlog to run AFTER W1-W10, so style lands on final code rather than code about to be rewritten. Per-cluster detail persisted in `sweep-findings.json` (session memory dir).

### W1 — Glossary + targeted renames

Lock the glossary above, then rename the worst conflations. The bulk of the renames ride inside W2 so the agent files are touched once. Scope: the triple-meaning `IAgent`/`AcpAgent` runtime role becomes `*Runner`; the `ACP/` plugin folder + namespace becomes `Agents/` (the protocol library keeps the ACP name); `Conversation.SessionId` becomes `AgentSessionId`. Watch the serialization strings in `ConversationDto` / persisted settings keys: rename C# identifiers without changing the on-disk keys, or migrate deliberately.

### W2 — ACP convergence + conformance + Codex verify (the load-bearing refactor)

Make ACP the single internal seam. Compose a single sealed `StreamJsonAgent` (process spawn/resolve, turn gating, `--session-id`/`--resume`, read loop, error capture) with a per-agent `IStreamJsonParser` (a small strategy interface, or injected delegates) rather than an abstract base that Claude and Codex subclass: favour composition and `sealed` over open behavioural inheritance, per `code-style.md`. The parser turns raw stream-json lines into ACP `session/update` into `RhinoAcpClient`, exactly as Gemini's native path already does; with two implementations (Claude, Codex) the interface is earned, not speculative. `AgentFactory` returns runners that all converge on the same client. Verify Codex against its real CLI and clear the `// verify:` markers in `CodexCliAgent.cs`. Fold the C# conformance pass in while these files are open: match each absence/failure to its channel (`T?` for genuine absence, `TryGet`, a sealed `Result` DU, or a fail-fast `throw`), kill the `Connection!` null-forgiving and any sentinel / `bool?` smells, properties over readonly fields, async hygiene (`ConfigureAwait(false)` in the `rhino/acp` library, no `async void` outside event handlers, never block on async, thread the `CancellationToken`), and drop abstraction that doesn't earn its place. Touch points: `CliAgent.cs`, `ClaudeAcpAgent.cs`, `CodexCliAgent.cs`, `AcpAgent.cs`, `GeminiConnection.cs`, `RhinoAcpClient.cs`, `AgentFactory.cs`.

### W3 — "Just GO" onboarding

On panel open: probe PATH and standard install dirs for `claude`, auto-wire the rhino MCP server, auto-start the listener, zero settings on the happy path. If nothing is found, the panel points at the docs page on installing and authing an agent (no bundled installer). When an agent is ready and the conversation is empty, show a few clickable starter prompt chips (GH2-flavored) that run on click, so the first prompt is one tap away. Touch points: `AgentRegistry.cs` (probing/resolution), `AgentHost.cs`, the AIPanel no-agent and empty-conversation states.

### W4 — Grasshopper authoring (R9 / GH2)

Build the full agentic authoring loop on Rhino 9 / GH2: the agent builds a graph from a plain-language description, solves it, reads back errors and warnings, self-corrects, and reports the result. The enabling piece is surfacing solve errors/warnings as a structured tool result the agent can act on (not just a success/fail). Harden the GH2 tools and the system prompt for this loop; because grounding is pull-only, the prompt must steer the agent to check current selection / active view / graph state before acting. Keep GH1 carried along for R8. Touch points: `rhino/plugin/Tools/GH2/*` (esp. solve + error read-back), `AgentPrompts.cs`.

### W5 — Streaming + UI polish

Replace the full-rebuild-per-event render with incremental updates while keeping the `UI = f(state)` model (diff the new state against the rendered state, React-style, not two-way binding), per `code-style.md`; smooth streaming; add stop / regenerate / edit-resend. Render tool chips as a human-readable summary by default ("added 3 curves", "placed 5 components") with the raw JSON args/result behind the expander; this needs each tool result to carry a concise summary (or a per-tool formatter). Keep the custom Drawable `MessageBubble` + `TranscriptViewModel` (Eto GridView has no variable row height). Markdown and inline geometry/viewport previews are deferred. Also in this UI workstream: a subtle per-turn/session token indicator (read from the stream-json `result` usage), keyboard shortcuts (Esc stops a turn, a shortcut to focus the prompt, new-conversation shortcut), an unread indicator, and a turn-complete cue when the panel isn't focused. Touch points: `AIPanel.cs` (Rerender + header/footer), `MessageBubble.cs`, `TranscriptViewModel.cs`, plus a summary field on tool results.

### W6 — Per-turn undo checkpoint

Group each agent turn's document mutations into a single undo record so one Ctrl+Z reverts the whole turn, with no per-call prompt. Zero-friction safety net pairing with auto-grant. Touch points: the turn boundary around tool execution (`AgentDispatch` / the MCP tool-call handler), using `RhinoDoc.BeginUndoRecord` / `EndUndoRecord`.

### W7 — Unit coverage

Grow coverage alongside every workstream: ACP protocol round-trips, `AgentDispatch`, `AgentRegistry` resolution + probing, `Conversation` + `ConversationStore` round-trip and pruning, `AcpMessageMapper`. No headless Rhino in CI yet. Touch points: `tests/Acp.Tests`, `tests/Server.Tests`, new fixtures with a loopback/fake agent (a real second implementation of the parser/connection contract, classicist-style, not a mock).

### W8 — Version source-of-truth

Single version for router + plugin from one place (`Directory.Build.props` or a generated file) propagated to `manifest.yml` and the csproj so the three values can't drift; connector and cc-plugin version on their own triggers. Removes the hand-duplicated 0.1.3.

### W9 — Grounding support (pull-only)

Add an always-on grounding steer to `AgentPrompts` (check selection/view before acting on existing geometry; read the canvas before GH edits) and a single `get_context` MCP tool that returns the current selection + active view + a doc/graph summary in one round-trip. Keeps the pull-only model but removes the blind-agent and multi-call-latency costs. The GH loop is already served by existing pulls (`get_canvas_graph`, `solve`); this targets general modeling tasks. Touch points: `AgentPrompts.cs`, a new `Tools/` context tool.

### W10 — Conversation resume

Let the user load a past conversation from Prev Convos and keep talking, not just review it. Restore the stored `Conversation`, set the runner's agent session id to the saved one, and launch the CLI with `--resume` so the agent continues with its context. Touch points: `ConversationStore` (load), `AgentHost` / `AgentRegistry` (adopt a saved session id), the runner's session/resume path, the AIPanel Prev Convos action.

### W11 — ask_user redesign (non-blocking, persistent, in-panel-only)

Today ask_user is a blocking MCP tool: it `await`s a TaskCompletionSource until the user answers, which collides with the MCP client's per-tool-call liveness timeout (claude's default 60s; band-aided to 1h in b80131a) and drops the answer when the request is aborted. Decided redesign:

- Non-blocking, ANSWER-AS-NEXT-TURN (in-panel only). `ask_user(question, options, multiSelect)` persists the question, renders BOTH a panel card AND a command-line GetOption picker, and returns immediately with a result that tells the agent to STOP and end its turn; the user's answer is delivered as the agent's NEXT prompt (the same live pooled agent resumes with it). No held HTTP request, so no timeout collision, robust to any wait / panel reload / idle.
- Command-line channel = GetOption picker (the clickable option UX restored). In the non-blocking model GetOption is an ANSWER AFFORDANCE, not a blocking tool-wait (the tool already returned): it runs on the UI thread presenting clickable options; picking one dispatches that choice as the next prompt; a panel click cancels the running GetOption (first-wins). Caveats to handle: UI-thread invocation, cancel-vs-panel coordination, and GetOption fired outside a command is cross-platform fragile (verify on Mac + Windows live). This replaces the typed-`"..."` CommandInterceptor.AnswerPending path.
- Hold the pending question on the live Conversation as transient UI state, so it survives a panel dock/undock reload (the panel rebinds to the same live Conversation instance) and is answerable anytime until the answer turn is sent, when the card clears. It is deliberately NOT serialized (a half-asked question must never persist); on an agent/conversation rebuild the agent simply re-poses if it still needs the answer. The live Conversation is the single source of truth: no separate registry.
- EXTERNAL callers unsupported: exclude ask_user from the external (/) endpoint; an external (router) caller gets a clear "only available to the in-Rhino panel agent" result. External agents (Claude Desktop/Code) use their own client's question UI.
- Removes the blocking await + TaskCompletionSource race, the AskUserRegistry entirely, and CommandInterceptor.AnswerPending (answers are normal next-turn prompts now); makes the MCP_TOOL_TIMEOUT band-aid unnecessary for ask_user (it stays only as a guard for other genuinely-slow tools).
- Update AgentPrompts.AskUserSteer to: call ask_user, then STOP and end the turn; the user's answer comes as their next message, then continue.
- RISK: relies on the agent reliably ending its turn after ask_user. Mitigate with the strong steer + explicit "STOP and wait" tool-result wording; verify with a live in-panel run.

Touch points: rhino/plugin/Tools/AskUserTool.cs, AskUserQuestion.cs, AskUserPicker.cs (command-line answer affordance), rhino/plugin/Agents/CommandInterceptor.cs, AgentPrompts.cs, rhino/plugin/UI/AIPanel.cs (question card render + answer-composes-next-prompt), the in-panel-only endpoint filtering (Server/McpEndpoint.cs / the /agent vs / split), and the pending-question home on the live Conversation (transient UI state, not ConversationStore).

Out-of-band: privacy is a small docs edit (PRIVACY.md + site); transcripts need no work (decision: keep PersistentSettings); multi-agent-per-doc pooling stays as-is unless we choose to simplify later.

## Risks

- W2 is load-bearing: onboarding, GH authoring, and tests all sit on the converged seam. Gemini's working native path de-risks the contract, but the Claude/Codex translator extraction is the real surgery.
- Codex verification depends on its actual CLI contract; the `// verify:` markers are unvalidated assumptions until run against the real binary.
- Translator fragility: the `claude` stream-json format is undocumented. We accept owning it (in exchange for zero extra dependencies); guard the parser and fail soft on unknown event shapes.
- R9 / GH2 is WIP and moving under us; the headline bets on a platform that is still changing.
- Per-turn undo grouping must wrap mutations that happen inside the MCP tool handler, not just in the panel; verify the undo record spans the whole turn including scripted edits.
- Auto-grant means arbitrary code execution and destructive edits run without confirmation; the undo checkpoint is the only safety net, so it has to be reliable.
- PersistentSettings transcript bloat: the 50-cap bounds count, not size; large tool I/O per turn can still grow the store. Revisit if it bites.

## Verification

1. Build: `dotnet build rhino/plugin/RhMcp.csproj -p:RhinoTarget=R8 -p:TargetFramework=net8.0` (R9 for the GH2 work).
2. Convergence: Claude, Gemini, and Codex all stream through the same ACP `RhinoAcpClient` path; the single sealed `StreamJsonAgent` (composed with per-agent parsers) is the only place process lifecycle lives.
3. Codex: a real turn against the actual Codex CLI streams tool calls and text correctly; no `// verify:` markers remain.
4. Onboarding: fresh machine with only `claude` installed and authed → open the panel → it just works with zero settings. Remove `claude` → panel shows the docs link, no crash.
5. GH2 demo: a plain-language prompt builds a GH2 graph on Rhino 9; when a component errors, the agent reads the solve error/warning and self-corrects without the user stepping in.
6. Undo: after an agent turn that creates and edits geometry, one Ctrl+Z reverts the entire turn.
7. Streaming: long responses render incrementally without a full rebuild per event; stop, regenerate, and edit-resend work.
8. Tests: new unit coverage for registry resolution, conversation round-trip + pruning, and message mapping is green in CI.
9. Versioning: bumping the single source updates the plugin and router; the connector and cc-plugin are untouched.
10. Discoverability: an empty conversation with a ready agent shows clickable starter prompt chips; clicking one runs it.
11. Result view: tool chips read as human summaries ("added 3 curves") with raw JSON available on expand.
12. Grounding: "fillet the selected objects" acts on the current selection without the agent asking which; `get_context` returns selection + view in one call.
13. Resume: reopen a past conversation, send a follow-up, and the agent continues with its prior context.
14. Usage: a subtle token/cost indicator updates per turn.
15. Quality of life: Esc stops a running turn; the panel flags unread output and signals turn completion when it isn't focused.

---

## Execution via Workflows

This section is *how* we build the workstreams above, using the Workflow tool. Structured as three staged workflows with a human gate between each: you review a stage's result before the next launches.

### The set (per workstream)

Each workstream is built by a four-role set, mapped to this session's agent types:

| Role      | Agent type | Mutates files? | Job |
| --------- | ---------- | -------------- | --- |
| architect | Plan       | no  | file-level design: signatures, touch points, edge cases, test plan |
| maker     | claude     | yes | implement the design; done-criteria includes a green build |
| review    | (fleet below) | no | three focused checks on the diff |
| fixer     | fixer      | yes | apply findings; review ⇄ fixer loop, cap ~3 rounds |

The review fleet is three checks (run per workstream and again on the integrated whole):
- Bug check (`investigator-bug`): wrong operators, off-by-one, swallowed exceptions, async/threading mistakes.
- Concept-validity check (custom-prompted reviewer on our domain, not the stock concept investigator): ACP stays the single seam; agent vs runner vs definition; conversation/turn/session semantics; no layer leaks.
- Coding-philosophy check (custom reviewer; read `~/.claude/code-style.md` in full as the authority, do not rely on summaries): the load-bearing rules for this codebase are no `var`, explicit access modifiers, properties over readonly fields, channel-matched absence/failure (`T?` for genuine absence is fine; flag `!` null-forgiving, sentinels, `bool?`, throwing for control flow), `sealed` + composition over open behavioural inheritance, async hygiene (`ConfigureAwait(false)` in library code, no `async void` outside handlers, no blocking on async, thread `CancellationToken`), modern C# (switch expressions, records, pattern matching, collection expressions), minimal earned abstraction (no speculative or one-implementation interfaces), dumb immutable data objects, and `UI = f(state)` over two-way binding.

### The constraint that shapes the stages

Workflow worktrees are per-agent. An architect → maker → review ⇄ fixer loop can only share state if those agents run on the same working tree, which means sequentially with no worktree. Worktrees pay off only for independent, parallel file mutation, where each lane is a single build-gated maker and review is deferred. File hotspots (`AIPanel.cs` in W3/W5/W10, `AgentPrompts.cs` in W4/W9, `AgentRegistry`/`AgentHost` in W3/W10) therefore run in the sequential lane; W4 (scoped to its own GH prompt fragment), W6, and W8 are independent enough for parallel worktrees.

### Stages

```
Stage S — Great Sweep      whole rhino/ C# (bug · concept · philosophy · dead-code)
   pre-screen ▸ investigate (file,check) ▸ cluster ──► GATE (approve clusters)
   fix per cluster ⇄ reviewer ▸ build gate ▸ integration R8 + R9 + tests ──► all-green baseline

Stage 0 — Spine            sequential, shared tree, full set
   W1 ▸ W2  ──[build R8]──► review ⇄ fixer ──► MERGE + GATE
   decompose W2: sealed StreamJsonAgent + IStreamJsonParser ▸ Claude parser ▸ Codex verify ▸ Gemini wire ▸ conformance

Stage 1 — Fan-out
   parallel worktrees (build-gated only):   W4 (GH) │ W6 (undo) │ W8 (version)
   sequential on branch (full set):         W3 ▸ W9 ▸ W10 ▸ W5   (AIPanel / AgentPrompts hotspots)
                                            ──► MERGE + GATE

Stage 2 — Integration      sequential
   full build R8 + R9 ▸ unit tests ▸ verification 1-15 ▸ 3-check review ⇄ fixer ▸ completeness critic ──► GATE / demo
```

### Workflow primitives

- `phase()` per stage; `pipeline()` for the sequential architect → maker → review → fix chains; `parallel()` only for the independent worktree lanes.
- Schemas for the handoffs: design doc (architect), findings (review), verdict (fixer).
- `isolation: 'worktree'` only on the independent lanes' makers (W4/W6/W8); everything in a review loop runs on the shared tree.
- Build gate is the maker's done-criteria: R8 `dotnet build rhino/plugin/RhMcp.csproj -p:RhinoTarget=R8 -p:TargetFramework=net8.0`; R9 for the GH2 work (`-p:RhinoTarget=R9`).

### Kicking it off

When ready, say the word (include "workflow") and I author and launch Stage 0 as one Workflow call, report the result, you gate, then we proceed stage by stage. Nothing runs until you opt in.
