import { bind, each, el, onCleanup, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { clockTime, formatTokens, relativeTime } from '../state/format.js';
import { t } from '../i18n/t.js';
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
      ? el(
          'button',
          { type: 'button', 'aria-label': () => t('chip.removeNamed', item.label), onClick: removable },
          icon('close', 11),
        )
      : el(
          'button',
          {
            type: 'button',
            'aria-label': () => t('chip.revealNamed', item.label),
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
        el('div', { class: 'plan-head', text: () => t('plan.head') }),
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

function replyText(turn: TurnView): string {
  return turn
    .blocks()
    .filter((view) => view.kind === 'text')
    .map((view) => view.text.peek())
    .join('\n\n');
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
            return value ? t('history.tokenCount', formatTokens(value.inputTokens + value.outputTokens)) : '';
          },
        }),
      ],
    ),
    el('span', { class: 'spacer' }),
    el(
      'button',
      { type: 'button', title: () => t('turn.copy'), onClick: () => ctx.copy(replyText(turn)) },
      icon('copy', 13),
    ),
    when(
      () => turn.status() !== 'running',
      () =>
        el(
          'button',
          { type: 'button', title: () => t('turn.retry'), onClick: () => ctx.send({ type: 'turn.retry', turnId: turn.id }) },
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
            title: () => t('turn.undoTitle'),
            onClick: () => ctx.send({ type: 'turn.undo', turnId: turn.id }),
          },
          icon('undo', 13),
          el('span', { text: () => t('turn.undo') }),
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
      () => el('div', { class: 'lifecycle', text: () => t('turn.stopped') }),
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

  // Deliberately not scrollTop = scrollHeight. The panel carries a CSS zoom (0.9 at 100%, 1.8 at the
  // top of the ladder), and an engine that reports scrollHeight in the zoomed scale but clamps
  // scrollTop in the unzoomed one turns that assignment into a short hop: at 200% it stops about
  // halfway. Any value past the end clamps to the real bottom in every scale.
  const PAST_THE_END = 1e7;
  const toBottom = (): void => {
    scroller.scrollTop = PAST_THE_END;
  };

  // A replay is over when the transcript has held its height for this long; the cap is there for
  // content that never settles, so a stuck landing can never own the scroller for good.
  const SETTLED_MS = 1200;
  const LANDING_CAP_MS = 20000;

  let landing = 0;
  function stopLanding(): void {
    if (landing === 0) return;
    cancelAnimationFrame(landing);
    landing = 0;
  }

  function land(): void {
    stopLanding();
    const giveUpAt = Date.now() + LANDING_CAP_MS;
    let height = -1;
    let grewAt = Date.now();
    const step = (): void => {
      landing = 0;
      toBottom();
      const now = Date.now();
      if (scroller.scrollHeight !== height) {
        height = scroller.scrollHeight;
        grewAt = now;
      }
      if (now - grewAt < SETTLED_MS && now < giveUpAt) landing = requestAnimationFrame(step);
    };
    landing = requestAnimationFrame(step);
  }

  // Scroll events are not a reliable signal of intent. Our own autoscroll produces them, and so
  // does the browser's scroll anchoring when the composer resizes or content lands above the
  // viewport, which is what used to unpin the transcript mid-stream and strand the user halfway up.
  // So unpinning requires a real gesture, while reaching the bottom always re-pins.
  let intentUntil = 0;
  const noteIntent = (): void => {
    intentUntil = Date.now() + 400;
    stopLanding();
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
    if (ui.pinned.peek()) toBottom();
    // Only agent output counts as unread: the user expanding a card grew the content too.
    else if (store.running.peek()) ui.hasNew.set(true);
  });
  observer.observe(stream);
  onCleanup(() => observer.disconnect());

  // A snapshot replaces the whole transcript (resume, load, reconnect), and where the user had
  // scrolled to in the old one means nothing in the new one: it has to land on the newest message,
  // however they left the last conversation.
  //
  // One jump cannot do that, because the transcript is nowhere near its final height when the
  // snapshot lands: the host replays the turns one event at a time, and every agent message renders
  // its markdown a frame after its text arrives. So hold the view against the bottom until the
  // content stops growing, and let only a real gesture out of it.
  bind(() => {
    store.session();
    ui.pinned.set(true);
    ui.hasNew.set(false);
    land();
  });
  onCleanup(stopLanding);

  const jumpToLatest = () => {
    ui.pinned.set(true);
    ui.hasNew.set(false);
    scroller.scrollTo({ top: PAST_THE_END, behavior: 'smooth' });
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
            el('span', { text: () => t('transcript.newOutput') }),
          ),
        ),
    ),
  );
}
