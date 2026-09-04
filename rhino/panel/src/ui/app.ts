import { el, onCleanup, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { clockTime } from '../state/format.js';
import { agentMenu } from './agentMenu.js';
import { composer } from './composer.js';
import { hostMenu } from './hostMenu.js';
import type { PanelContext } from './context.js';
import { header } from './header.js';
import { historyDrawer } from './history.js';
import { icon } from './icons.js';
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

  // One step per gesture, not per wheel tick: a trackpad pinch fires dozens of events.
  let lastWheelStep = 0;

  const onKeyDown = (event: KeyboardEvent): void => {
    const mod = event.metaKey || event.ctrlKey;

    // Match against code as well as key: on several layouts "+" needs Shift, and the numpad
    // reports different key values again.
    if (mod) {
      const zoomIn = event.key === '+' || event.key === '=' || event.code === 'Equal' || event.code === 'NumpadAdd';
      const zoomOut = event.key === '-' || event.key === '_' || event.code === 'Minus' || event.code === 'NumpadSubtract';
      const zoomReset = event.key === '0' || event.code === 'Digit0' || event.code === 'Numpad0';

      if (zoomIn || zoomOut || zoomReset) {
        event.preventDefault();
        if (zoomIn) ctx.zoom.in();
        else if (zoomOut) ctx.zoom.out();
        else ctx.zoom.reset();
        return;
      }
    }

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

  const onWheel = (event: WheelEvent): void => {
    if (!event.ctrlKey && !event.metaKey) return;
    event.preventDefault();
    const now = Date.now();
    if (now - lastWheelStep < 110) return;
    lastWheelStep = now;
    if (event.deltaY < 0) ctx.zoom.in();
    else if (event.deltaY > 0) ctx.zoom.out();
  };

  // Both on the window rather than the panel element. After a menu item is clicked its button is
  // gone and focus falls back to the body, whose key events never reach a handler bound to the
  // panel div; and inside Rhino the webview is the panel, so there is nowhere else for a wheel
  // gesture to land. passive:false is required or preventDefault is ignored and the page scrolls.
  hostMenu(ctx);
  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('wheel', onWheel, { passive: false });
  onCleanup(() => {
    window.removeEventListener('keydown', onKeyDown);
    window.removeEventListener('wheel', onWheel);
  });

  return el(
    'div',
    {
      class: 'panel',
      ref: (node: HTMLElement) => ctx.zoom.attach(node),
    },
    header(ctx),
    transcript(ctx),
    // Below the transcript, not above it: what the agent is doing now belongs next to where its
    // output is landing and next to the Stop button, not up by the agent picker.
    statusStrip(ctx),
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
  );
}
