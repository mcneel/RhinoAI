import { el, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { clockTime } from '../state/format.js';
import { agentMenu } from './agentMenu.js';
import { composer } from './composer.js';
import type { PanelContext } from './context.js';
import { header } from './header.js';
import { historyDrawer } from './history.js';
import { icon } from './icons.js';
import { notices } from './notices.js';
import { transcript } from './transcript.js';

function statusStrip(ctx: PanelContext): Child {
  const { store } = ctx;
  const text = () => store.status() ?? (store.thinking() ? 'Working…' : null);
  return when(
    () => text() !== null,
    () =>
      el(
        'div',
        { class: 'status-strip', role: 'status' },
        el('span', { class: 'spark' }, icon('sparkle', 13)),
        el('span', { class: 'shimmer', text: () => text() ?? '' }),
      ),
  );
}

function reviewBar(ctx: PanelContext): Child {
  const session = () => ctx.store.session();
  return el(
    'div',
    { class: 'review-bar' },
    icon('history', 14),
    el('span', { text: () => `Read-only · ${clockTime(session()?.startedAt ?? '')}` }),
    el('span', { class: 'spacer' }),
    el(
      'button',
      { class: 'btn', type: 'button', onClick: () => ctx.send({ type: 'conversation.exitReview' }) },
      'Back to live',
    ),
    el(
      'button',
      {
        class: 'btn primary',
        type: 'button',
        title: 'Continue this conversation with its agent',
        onClick: () => {
          const id = session()?.sessionId;
          if (id) ctx.send({ type: 'conversation.resume', sessionId: id });
        },
      },
      'Resume',
    ),
  );
}

export function app(ctx: PanelContext): Child {
  const { store, ui } = ctx;

  const onKeyDown = (event: KeyboardEvent): void => {
    const mod = event.metaKey || event.ctrlKey;
    if (event.key === 'Escape' && ui.overlay() !== 'none') {
      ui.closeOverlay();
      event.preventDefault();
      return;
    }
    if (mod && event.shiftKey && event.key.toLowerCase() === 'n') {
      ctx.send({ type: 'conversation.new' });
      event.preventDefault();
    }
  };

  return el(
    'div',
    { class: 'panel', onKeyDown },
    header(ctx),
    statusStrip(ctx),
    transcript(ctx),
    when(
      () => store.readOnly(),
      () => reviewBar(ctx),
      () => composer(ctx),
    ),
    when(
      () => ui.overlay() === 'agents',
      () => [el('div', { class: 'scrim', onClick: () => ui.closeOverlay() }), agentMenu(ctx)],
    ),
    when(
      () => ui.overlay() === 'history',
      () => historyDrawer(ctx),
    ),
    notices(ctx),
  );
}
