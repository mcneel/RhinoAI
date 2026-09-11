// The panel <-> host wire protocol.
//
// The Eto panel subscribed to Conversation.Changed, re-read the whole graph, flattened it, and
// diffed the result against what it had already drawn. Every interesting bug lived in that loop.
// Here the host says what changed: a text delta names the block it extends, a tool result names the
// call it completes. `conversation` (a full snapshot) exists only for load / resume / reconnect.

import type { IconName } from '../ui/icons.js';

// ---------------------------------------------------------------- values

export interface TokenUsage {
  inputTokens: number;
  outputTokens: number;
  /** null means the agent reports tokens but not money. Not zero. */
  costUsd: number | null;
}

export const NO_USAGE: TokenUsage = { inputTokens: 0, outputTokens: 0, costUsd: null };

export type AgentAvailability = 'ready' | 'disabled' | 'missing' | 'signin';

export interface AgentInfo {
  name: string;
  label: string;
  model: string;
  modelLabel: string;
  availability: AgentAvailability;
  /** Why it is not ready, in words the user can act on. */
  detail?: string;
  builtin: boolean;
}

export type AttachmentKind = 'image' | 'text';

export interface Attachment {
  id: string;
  kind: AttachmentKind;
  name: string;
  mediaType: string;
  bytes: number;
  /** The file's bytes, base64 in a data URL: what the host sends to the agent, and what an image thumbnail draws. */
  dataUrl?: string;
}

/** Live document context the user can @-mention into a prompt. */
export type ContextKind = 'selection' | 'layer' | 'view' | 'document' | 'block' | 'grasshopper' | 'file';

export interface ContextItem {
  id: string;
  kind: ContextKind;
  label: string;
  detail?: string;
  count?: number;
}

/** `unknown`: the turn ended without the call ever reporting, so it may or may not have run. */
export type ToolStatus = 'running' | 'ok' | 'failed' | 'denied' | 'unknown';

/** A payload the panel renders as something better than JSON. */
export type ToolPreview =
  | { kind: 'image'; dataUrl: string; caption?: string }
  | { kind: 'code'; language: string; text: string }
  | { kind: 'objects'; items: { id: string; label: string; layer?: string }[] }
  | { kind: 'graph'; components: string[]; wires: number; errors: number; warnings: number }
  | { kind: 'table'; columns: string[]; rows: string[][] };

/** An extra button on a tool card, authored host-side so the panel keeps no per-tool knowledge. */
export interface ToolChip {
  id: string;
  label: string;
  icon?: IconName;
  style?: 'default' | 'danger';
}

export interface ToolCall {
  id: string;
  /** Wire name, e.g. `g2_place_component`. */
  name: string;
  /** Host-authored human phrase, e.g. "placed Circle". */
  title: string;
  args: unknown;
  status: ToolStatus;
  result?: unknown;
  error?: string;
  startedAt: string;
  durationMs?: number;
  preview?: ToolPreview;
  /** Set when the tool mutated the document, so the turn can offer an undo. */
  mutated?: boolean;
  /** Extra buttons for this call. The host clears it on completion, so a chip cannot outlive its use. */
  chips?: ToolChip[];
}

export type ToolPatch = Partial<Pick<ToolCall, 'status' | 'result' | 'error' | 'durationMs' | 'preview' | 'title' | 'mutated' | 'chips'>>;

export type NoticeLevel = 'info' | 'warn' | 'error';

export type TurnStatus = 'running' | 'ok' | 'cancelled' | 'error';

export interface PlanStep {
  id: string;
  text: string;
  state: 'pending' | 'active' | 'done' | 'skipped';
}

export interface PendingQuestion {
  id: string;
  question: string;
  options: string[];
  mode: 'single' | 'multi';
  allowOther: boolean;
}

export interface HistoryEntry {
  sessionId: string;
  title: string;
  agent: string;
  docTitle: string;
  startedAt: string;
  turns: number;
  usage: TokenUsage;
  resumable: boolean;
}

// ---------------------------------------------------------------- transcript

export type BlockSnapshot =
  | { kind: 'text'; id: string; text: string; at: string }
  | { kind: 'tool'; id: string; call: ToolCall }
  | { kind: 'notice'; id: string; level: NoticeLevel; text: string };

export interface TurnSnapshot {
  id: string;
  prompt: string;
  attachments: Attachment[];
  context: ContextItem[];
  startedAt: string;
  status: TurnStatus;
  usage: TokenUsage | null;
  blocks: BlockSnapshot[];
  plan: PlanStep[];
  /** The turn opened an undo record, so "revert this turn" is a single Rhino undo. */
  undoable: boolean;
  error?: string;
}

export interface ConversationSnapshot {
  sessionId: string;
  agent: string;
  docTitle: string;
  startedAt: string;
  turns: TurnSnapshot[];
  /** A reviewed past conversation: the composer is replaced by Resume. */
  readOnly: boolean;
}

export interface HostInfo {
  product: string;
  version: string;
  platform: 'windows' | 'macos';
  docTitle: string;
  capabilities: {
    attachments: boolean;
    viewportCapture: boolean;
    undoTurn: boolean;
    grasshopper: boolean;
  };
}

// ---------------------------------------------------------------- host -> panel

export type HostEvent =
  | { type: 'hello'; host: HostInfo }
  | { type: 'theme'; scheme: 'light' | 'dark'; tokens?: Record<string, string> }
  | { type: 'agents'; agents: AgentInfo[]; active: string | null }
  | { type: 'context'; items: ContextItem[] }
  | { type: 'history'; entries: HistoryEntry[] }
  | { type: 'conversation'; snapshot: ConversationSnapshot }
  | { type: 'turn.begin'; turn: TurnSnapshot }
  | { type: 'turn.text'; turnId: string; blockId: string; delta: string }
  | { type: 'turn.tool'; turnId: string; call: ToolCall }
  | { type: 'turn.tool.patch'; turnId: string; callId: string; patch: ToolPatch }
  | { type: 'turn.plan'; turnId: string; steps: PlanStep[] }
  | { type: 'turn.usage'; turnId: string; usage: TokenUsage }
  | { type: 'turn.end'; turnId: string; status: TurnStatus; error?: string }
  | { type: 'question'; question: PendingQuestion }
  | { type: 'question.clear'; id: string }
  | { type: 'attachments.add'; attachments: Attachment[] }
  | { type: 'notice'; level: NoticeLevel; text: string }
  | { type: 'status'; text: string | null }
  // The host owns the right-click menu, but the panel owns the zoom ladder, so the menu sends back
  // an intent rather than a level. `set` is the other direction: the level the host had stored.
  | { type: 'zoom'; action: 'in' | 'out' | 'reset' }
  | { type: 'zoom'; action: 'set'; level: number }
  | { type: 'reload' };

// ---------------------------------------------------------------- panel -> host

export interface PromptRequest {
  text: string;
  attachments: Attachment[];
  context: ContextItem[];
}

/** One question's answers within a submit. A submit carries every question currently showing. */
export interface QuestionAnswer {
  id: string;
  answers: string[];
}

export type PanelCommand =
  | { type: 'ready' }
  | { type: 'prompt'; request: PromptRequest }
  | { type: 'cancel' }
  | { type: 'conversation.new' }
  | { type: 'conversation.load'; sessionId: string }
  | { type: 'conversation.resume'; sessionId: string }
  | { type: 'conversation.exitReview' }
  | { type: 'agent.select'; name: string }
  | { type: 'agent.login' }
  | { type: 'question.answer'; items: QuestionAnswer[] }
  | { type: 'question.dismiss'; ids: string[] }
  | { type: 'tool.chip'; callId: string; chipId: string }
  | { type: 'turn.undo'; turnId: string }
  | { type: 'turn.retry'; turnId: string }
  | { type: 'context.refresh' }
  | { type: 'context.reveal'; id: string }
  | { type: 'attachments.pick' }
  | { type: 'attachments.drop'; files: { name: string; mediaType: string; dataUrl: string }[] }
  // The panel cannot store its own level (opaque origin, so localStorage throws), so the host keeps it.
  | { type: 'zoom.set'; level: number }
  | { type: 'settings.open' }
  | { type: 'url.open'; url: string }
  | { type: 'clipboard.write'; text: string }
  | {
      type: 'menu.open';
      x: number;
      y: number;
      canZoomIn: boolean;
      canZoomOut: boolean;
      canResetZoom: boolean;
      zoomLabel: string;
      selection: string;
    };
