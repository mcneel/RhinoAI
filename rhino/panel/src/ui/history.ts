import { computed } from '../core/signal.js';
import { each, el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { formatTokens, relativeTime } from '../state/format.js';
import type { HistoryEntry } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

export function historyDrawer(ctx: PanelContext): Child {
  const { store, ui } = ctx;

  const matches = computed<readonly HistoryEntry[]>(() => {
    const query = ui.historyQuery().trim().toLowerCase();
    const entries = store.history();
    if (!query) return entries;
    return entries.filter((entry) =>
      `${entry.title} ${entry.agent} ${entry.docTitle}`.toLowerCase().includes(query),
    );
  });

  const row = (entry: HistoryEntry): Child =>
    el(
      'button',
      {
        class: 'convo',
        type: 'button',
        onClick: () => {
          ctx.send({ type: 'conversation.load', sessionId: entry.sessionId });
          ui.closeOverlay();
        },
      },
      el('span', { class: 'title', text: entry.title }),
      el('span', { class: 'when', text: relativeTime(entry.startedAt) }),
      el(
        'span',
        { class: 'sub' },
        icon('agent', 11),
        el('span', { text: entry.agent }),
        el('span', { text: '·' }),
        el('span', { text: entry.docTitle }),
        el('span', { text: '·' }),
        el('span', { text: `${entry.turns} turn${entry.turns === 1 ? '' : 's'}` }),
        el('span', { text: '·' }),
        el('span', { text: `${formatTokens(entry.usage.inputTokens + entry.usage.outputTokens)} tok` }),
      ),
    );

  return el(
    'div',
    { class: 'drawer', role: 'dialog', 'aria-label': 'Conversation history' },
    el(
      'div',
      { class: 'drawer-head' },
      el('b', { text: 'Conversations' }),
      el(
        'button',
        { class: 'icon-btn', type: 'button', 'aria-label': 'Close', onClick: () => ui.closeOverlay() },
        icon('close', 14),
      ),
    ),
    el(
      'div',
      { class: 'search' },
      icon('search', 14),
      el('input', {
        type: 'search',
        placeholder: 'Search prompts, agents, models…',
        value: () => ui.historyQuery(),
        onInput: (event: Event) => ui.historyQuery.set((event.target as HTMLInputElement).value),
        ref: (input: HTMLInputElement) => requestAnimationFrame(() => input.focus()),
      }),
    ),
    el(
      'div',
      { class: 'drawer-list' },
      each(
        () => matches(),
        (entry) => entry.sessionId,
        row,
      ),
      () =>
        matches().length === 0
          ? el('div', { class: 'lifecycle', text: store.history().length === 0 ? 'No saved conversations yet' : 'Nothing matches' })
          : null,
    ),
  );
}
