// Host state, held as signals all the way down to the individual streaming text block.
//
// The point of putting signals inside the transcript model rather than around it: a `turn.text`
// delta calls `block.text.set(...)` and exactly one DOM text node updates. Nothing re-flattens,
// nothing re-diffs, no row is rebuilt, no width is re-measured.

import { computed, signal, type ReadSignal, type Signal } from '../core/signal.js';
import {
  type AgentInfo,
  type Attachment,
  type BlockSnapshot,
  type ContextItem,
  type HistoryEntry,
  type HostEvent,
  type HostInfo,
  type NoticeLevel,
  type PendingQuestion,
  type PlanStep,
  type TokenUsage,
  type ToolCall,
  type TurnSnapshot,
  type TurnStatus,
} from '../protocol/events.js';

export type BlockView =
  | { kind: 'text'; id: string; at: string; text: Signal<string> }
  | { kind: 'tool'; id: string; call: Signal<ToolCall> }
  | { kind: 'notice'; id: string; level: NoticeLevel; text: string };

export interface TurnView {
  id: string;
  prompt: string;
  attachments: readonly Attachment[];
  context: readonly ContextItem[];
  startedAt: string;
  status: Signal<TurnStatus>;
  usage: Signal<TokenUsage | null>;
  blocks: Signal<readonly BlockView[]>;
  plan: Signal<readonly PlanStep[]>;
  undoable: Signal<boolean>;
  error: Signal<string | undefined>;
}

export interface SessionView {
  sessionId: string;
  docTitle: string;
  startedAt: string;
  readOnly: boolean;
}

export interface Notice {
  id: string;
  level: NoticeLevel;
  text: string;
}

function blockFrom(snapshot: BlockSnapshot): BlockView {
  switch (snapshot.kind) {
    case 'text':
      return { kind: 'text', id: snapshot.id, at: snapshot.at, text: signal(snapshot.text) };
    case 'tool':
      return { kind: 'tool', id: snapshot.id, call: signal(snapshot.call) };
    case 'notice':
      return { kind: 'notice', id: snapshot.id, level: snapshot.level, text: snapshot.text };
  }
}

function turnFrom(snapshot: TurnSnapshot): TurnView {
  return {
    id: snapshot.id,
    prompt: snapshot.prompt,
    attachments: snapshot.attachments,
    context: snapshot.context,
    startedAt: snapshot.startedAt,
    status: signal(snapshot.status),
    usage: signal(snapshot.usage),
    blocks: signal<readonly BlockView[]>(snapshot.blocks.map(blockFrom)),
    plan: signal<readonly PlanStep[]>(snapshot.plan),
    undoable: signal(snapshot.undoable),
    error: signal(snapshot.error),
  };
}

let noticeSeq = 0;

export class Store {
  readonly host = signal<HostInfo | null>(null);
  readonly scheme = signal<'light' | 'dark'>('dark');
  readonly agents = signal<readonly AgentInfo[]>([]);
  readonly activeAgentName = signal<string | null>(null);
  readonly context = signal<readonly ContextItem[]>([]);
  readonly history = signal<readonly HistoryEntry[]>([]);
  readonly session = signal<SessionView | null>(null);
  readonly turns = signal<readonly TurnView[]>([]);
  readonly questions = signal<readonly PendingQuestion[]>([]);
  readonly notices = signal<readonly Notice[]>([]);
  readonly status = signal<string | null>(null);

  readonly activeAgent: ReadSignal<AgentInfo | null> = computed(() => {
    const name = this.activeAgentName();
    return this.agents().find((agent) => agent.name === name) ?? null;
  });

  readonly hasReadyAgent: ReadSignal<boolean> = computed(() =>
    this.agents().some((agent) => agent.availability === 'ready'),
  );

  readonly currentTurn: ReadSignal<TurnView | null> = computed(() => {
    const list = this.turns();
    return list.length > 0 ? (list[list.length - 1] as TurnView) : null;
  });

  readonly running: ReadSignal<boolean> = computed(() => this.currentTurn()?.status() === 'running');

  /** The agent is not thinking while it is blocked on unanswered questions. */
  readonly thinking: ReadSignal<boolean> = computed(() => this.running() && this.questions().length === 0);

  readonly readOnly: ReadSignal<boolean> = computed(() => this.session()?.readOnly === true);

  apply(event: HostEvent): void {
    switch (event.type) {
      case 'hello':
        this.host.set(event.host);
        // Scrollbar styling is Windows-only; see panel.css.
        document.documentElement.dataset['platform'] = event.host.platform;
        return;

      case 'theme':
        this.scheme.set(event.scheme);
        if (event.tokens)
          for (const [name, value] of Object.entries(event.tokens))
            document.documentElement.style.setProperty(`--${name}`, value);
        return;

      case 'agents':
        this.agents.set(event.agents);
        this.activeAgentName.set(event.active);
        return;

      case 'context':
        this.context.set(event.items);
        return;

      case 'history':
        this.history.set(event.entries);
        return;

      case 'conversation':
        this.session.set({
          sessionId: event.snapshot.sessionId,
          docTitle: event.snapshot.docTitle,
          startedAt: event.snapshot.startedAt,
          readOnly: event.snapshot.readOnly,
        });
        this.activeAgentName.set(event.snapshot.agent);
        this.turns.set(event.snapshot.turns.map(turnFrom));
        this.questions.set([]);
        return;

      // Idempotent on id. A host that re-announces a turn or a call must not be able to put two
      // rows with the same key into the transcript: the keyed list would track one and orphan the
      // other in the DOM, where nothing can ever remove it.
      case 'turn.begin':
        if (this.turns().some((turn) => turn.id === event.turn.id)) return;
        this.turns.set((turns) => [...turns, turnFrom(event.turn)]);
        return;

      case 'turn.text': {
        const turn = this.turn(event.turnId);
        if (!turn) return;
        const existing = turn
          .blocks()
          .find((block): block is Extract<BlockView, { kind: 'text' }> =>
            block.kind === 'text' && block.id === event.blockId,
          );
        if (existing) {
          existing.text.set((text) => text + event.delta);
          return;
        }
        turn.blocks.set((blocks) => [
          ...blocks,
          { kind: 'text', id: event.blockId, at: new Date().toISOString(), text: signal(event.delta) },
        ]);
        return;
      }

      case 'turn.tool': {
        const turn = this.turn(event.turnId);
        if (!turn) return;
        if (turn.blocks().some((block) => block.id === event.call.id)) return;
        turn.blocks.set((blocks) => [
          ...blocks,
          { kind: 'tool', id: event.call.id, call: signal(event.call) },
        ]);
        return;
      }

      case 'turn.tool.patch': {
        const block = this.turn(event.turnId)
          ?.blocks()
          .find((b): b is Extract<BlockView, { kind: 'tool' }> => b.kind === 'tool' && b.id === event.callId);
        block?.call.set((call) => ({ ...call, ...event.patch }));
        return;
      }

      case 'turn.plan':
        this.turn(event.turnId)?.plan.set(event.steps);
        return;

      case 'turn.usage':
        this.turn(event.turnId)?.usage.set(event.usage);
        return;

      case 'turn.end': {
        const turn = this.turn(event.turnId);
        if (!turn) return;
        turn.status.set(event.status);
        turn.error.set(event.error);
        return;
      }

      // Appended, not replaced: a second ask_user adds to the stack rather than evicting what is
      // already showing. Idempotent on id for the same reason turn.begin is.
      case 'question':
        this.questions.set((list) =>
          list.some((q) => q.id === event.question.id) ? list : [...list, event.question],
        );
        return;

      case 'question.clear':
        this.questions.set((list) => list.filter((q) => q.id !== event.id));
        return;

      case 'notice': {
        const notice: Notice = { id: `notice-${++noticeSeq}`, level: event.level, text: event.text };
        this.notices.set((list) => [...list, notice]);
        return;
      }

      case 'status':
        this.status.set(event.text);
        return;
    }
  }

  dismissNotice(id: string): void {
    this.notices.set((list) => list.filter((notice) => notice.id !== id));
  }

  private turn(id: string): TurnView | undefined {
    return this.turns().find((turn) => turn.id === id);
  }
}
