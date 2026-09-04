import { each, el, onCleanup, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { clockTime, formatTokens, relativeTime } from '../state/format.js';
import type { BlockView, TurnView } from '../state/store.js';
import type { Attachment, ContextItem, PlanStep } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { emptyState } from './empty.js';
import { icon } from './icons.js';
import { notices } from './notices.js';
import { agentMessage } from './message.js';
import { toolCard } from './toolCard.js';

const CONTEXT_ICON = {
  selection: 'cube',
  layer: 'layers',
  view: 'camera',
  document: 'document',
  block: 'cube',
  grasshopper: 'graph',
  file: 'document',
} as const;

function contextChip(ctx: PanelContext, item: ContextItem, removable?: () => void): Child {
  return el(
    'span',
    { class: 'chip', title: item.detail ?? item.label },
    icon(CONTEXT_ICON[item.kind], 12),
    el('span', { text: item.count !== undefined ? `${item.label} (${item.count})` : item.label }),
    removable
      ? el('button', { type: 'button', 'aria-label': `Remove ${item.label}`, onClick: removable }, icon('close', 11))
      : el(
          'button',
          {
            type: 'button',
            'aria-label': `Show ${item.label} in Rhino`,
            onClick: () => ctx.send({ type: 'context.reveal', id: item.id }),
          },
          icon('reveal', 11),
        ),
  );
}

function attachmentChip(attachment: Attachment): Child {
  return el(
    'span',
    { class: 'chip', title: attachment.name },
    attachment.kind === 'image' ? icon('camera', 12) : icon('document', 12),
    el('span', { text: attachment.name }),
  );
}

const PLAN_MARKER: Record<PlanStep['state'], string> = {
  pending: '○',
  active: '▸',
  done: '✓',
  skipped: '-',
};

function planStrip(turn: TurnView): Child {
  return when(
    () => turn.plan().length > 0,
    () =>
      el(
        'div',
        { class: 'plan' },
        el('div', { class: 'plan-head', text: 'Plan' }),
        each(
          () => turn.plan(),
          (step) => step.id,
          (step) =>
            el(
              'div',
              { class: `plan-step ${step.state}` },
              el('span', { class: 'marker', text: PLAN_MARKER[step.state] }),
              el('span', { class: 'label', text: step.text }),
            ),
        ),
      ),
  );
}

function block(ctx: PanelContext, turn: TurnView, view: BlockView): Child {
  switch (view.kind) {
    case 'text':
      return agentMessage(ctx, view.text, () => {
        const blocks = turn.blocks();
        return turn.status() === 'running' && blocks[blocks.length - 1]?.id === view.id;
      });
    case 'tool':
      return toolCard(ctx, view.call);
    case 'notice':
      return el('div', { class: 'lifecycle', text: view.text });
  }
}

function plainText(turn: TurnView): string {
  const parts = [`> ${turn.prompt}`];
  for (const view of turn.blocks()) {
    if (view.kind === 'text') parts.push(view.text.peek());
    else if (view.kind === 'tool') parts.push(`[${view.call.peek().title}]`);
  }
  return parts.join('\n\n');
}

function turnFooter(ctx: PanelContext, turn: TurnView): Child {
  const usage = () => turn.usage();
  return el(
    'footer',
    { class: 'turn-foot' },
    el('span', { class: 'when', title: () => clockTime(turn.startedAt), text: () => relativeTime(turn.startedAt) }),
    when(
      () => usage() !== null,
      () => [
        el('span', { class: 'sep', text: '·' }),
        el('span', {
          // Tokens only. A running cost turns every prompt into a purchase decision, which is not
          // the relationship we want the user to have with the panel.
          text: () => {
            const value = usage();
            return value ? `${formatTokens(value.inputTokens + value.outputTokens)} tok` : '';
          },
        }),
      ],
    ),
    el('span', { class: 'spacer' }),
    el(
      'button',
      { type: 'button', title: 'Copy this exchange', onClick: () => ctx.copy(plainText(turn)) },
      icon('copy', 13),
    ),
    when(
      () => turn.status() !== 'running',
      () =>
        el(
          'button',
          { type: 'button', title: 'Ask again', onClick: () => ctx.send({ type: 'turn.retry', turnId: turn.id }) },
          icon('retry', 13),
        ),
    ),
    when(
      () => turn.undoable() && turn.status() !== 'running',
      () =>
        el(
          'button',
          {
            type: 'button',
            title: 'Revert every document change this turn made',
            onClick: () => ctx.send({ type: 'turn.undo', turnId: turn.id }),
          },
          icon('undo', 13),
          el('span', { text: 'Revert' }),
        ),
    ),
  );
}

function turnView(ctx: PanelContext, turn: TurnView): Child {
  return el(
    'article',
    { class: () => `turn ${turn.status()}` },
    when(
      () => turn.context.length > 0 || turn.attachments.length > 0,
      () =>
        el(
          'div',
          { class: 'chip-row', style: { 'justify-content': 'flex-end' } },
          ...turn.context.map((item) => contextChip(ctx, item)),
          ...turn.attachments.map(attachmentChip),
        ),
    ),
    turn.prompt ? el('div', { class: 'msg-user', text: turn.prompt }) : null,
    planStrip(turn),
    each(
      () => turn.blocks(),
      (view) => view.id,
      (view) => block(ctx, turn, view),
    ),
    when(
      () => turn.status() === 'error' && turn.error !== undefined,
      () => el('div', { class: 'turn-error' }, icon('alert', 14), el('span', { text: () => turn.error() ?? '' })),
    ),
    when(
      () => turn.status() === 'cancelled',
      () => el('div', { class: 'lifecycle', text: 'stopped' }),
    ),
    turnFooter(ctx, turn),
  );
}

export function transcript(ctx: PanelContext): Child {
  const { store, ui } = ctx;

  const stream = el(
    'div',
    { class: 'stream' },
    when(
      () => store.turns().length === 0,
      () => emptyState(ctx),
      () =>
        each(
          () => store.turns(),
          (turn) => turn.id,
          (turn) => turnView(ctx, turn),
        ),
    ),
  );

  // Scroll events are not a reliable signal of intent. Our own autoscroll produces them, and so
  // does the browser's scroll anchoring when the composer resizes or content lands above the
  // viewport, which is what used to unpin the transcript mid-stream and strand the user halfway up.
  // So unpinning requires a real gesture, while reaching the bottom always re-pins.
  let intentUntil = 0;
  const noteIntent = (): void => {
    intentUntil = Date.now() + 400;
  };

  const scroller = el(
    'div',
    {
      class: 'transcript',
      tabindex: '-1',
      onScroll: () => {
        const gap = scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight;
        if (gap < 28) {
          ui.pinned.set(true);
          ui.hasNew.set(false);
          return;
        }
        if (Date.now() < intentUntil) ui.pinned.set(false);
      },
      onWheel: noteIntent,
      onPointerDown: noteIntent,
      onKeyDown: noteIntent,
      onTouchStart: noteIntent,
    },
    stream,
  );

  // Content height is the only signal that matters for autoscroll, and the browser already knows
  // it. No deferred layout pass, no AsyncInvoke, no "scroll after Eto settles".
  const observer = new ResizeObserver(() => {
    if (ui.pinned.peek()) scroller.scrollTop = scroller.scrollHeight;
    // Only agent output counts as unread: the user expanding a card grew the content too.
    else if (store.running.peek()) ui.hasNew.set(true);
  });
  observer.observe(stream);
  onCleanup(() => observer.disconnect());

  const jumpToLatest = () => {
    ui.pinned.set(true);
    ui.hasNew.set(false);
    scroller.scrollTo({ top: scroller.scrollHeight, behavior: 'smooth' });
  };

  return el(
    'div',
    { class: 'stage' },
    scroller,
    notices(ctx),
    when(
      () => !ui.pinned() && ui.hasNew(),
      () =>
        el(
          'div',
          { class: 'jump' },
          el(
            'button',
            { type: 'button', onClick: jumpToLatest },
            icon('arrowDown', 13),
            el('span', { text: 'New output' }),
          ),
        ),
    ),
  );
}
