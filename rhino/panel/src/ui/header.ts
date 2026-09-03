import { el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

export function header(ctx: PanelContext): Child {
  const { store, ui } = ctx;

  const agentButton = el(
    'button',
    {
      class: 'agent-chip',
      type: 'button',
      'aria-expanded': () => ui.overlay() === 'agents',
      'aria-label': 'Switch agent',
      onClick: () => ui.openOverlay('agents'),
    },
    el('span', {
      class: () => {
        const agent = store.activeAgent();
        if (store.thinking()) return 'dot busy';
        return `dot ${agent?.availability ?? 'missing'}`;
      },
    }),
    el(
      'span',
      { class: 'who' },
      el('span', { class: 'name', text: () => store.activeAgent()?.label ?? 'No agent' }),
      el('span', {
        class: 'model',
        text: () => store.activeAgent()?.modelLabel ?? 'nothing configured',
      }),
    ),
    el('span', { class: 'chev' }, icon('chevron', 13)),
  );

  return el(
    'header',
    { class: 'header' },
    agentButton,
    el(
      'button',
      {
        class: () => `icon-btn${ui.overlay() === 'history' ? ' on' : ''}`,
        type: 'button',
        title: 'Conversation history',
        onClick: () => ui.openOverlay('history'),
      },
      icon('history'),
    ),
    el(
      'button',
      {
        class: 'icon-btn',
        type: 'button',
        title: 'New conversation  (Ctrl+Shift+N)',
        disabled: () => store.running(),
        onClick: () => ctx.send({ type: 'conversation.new' }),
      },
      icon('plus'),
    ),
    el(
      'button',
      { class: 'icon-btn', type: 'button', title: 'AI settings', onClick: () => ctx.send({ type: 'settings.open' }) },
      icon('settings'),
    ),
  );
}
