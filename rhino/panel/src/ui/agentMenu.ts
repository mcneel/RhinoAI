import { each, el, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import type { AgentAvailability, AgentInfo } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

const REASON: Record<AgentAvailability, string> = {
  ready: '',
  disabled: 'turned off in settings',
  missing: 'not installed',
  signin: 'needs sign-in',
};

export function agentMenu(ctx: PanelContext): Child {
  const { store, ui } = ctx;

  const row = (agent: AgentInfo): Child => {
    const ready = agent.availability === 'ready';
    return el(
      'button',
      {
        class: () =>
          [
            'menu-item',
            ready ? '' : 'dim',
            store.activeAgentName() === agent.name ? 'selected' : '',
          ]
            .filter(Boolean)
            .join(' '),
        type: 'button',
        disabled: !ready,
        title: ready ? `Use ${agent.label}` : (agent.detail ?? REASON[agent.availability]),
        onClick: () => {
          ctx.send({ type: 'agent.select', name: agent.name });
          ui.closeOverlay();
        },
      },
      el('span', { class: `dot ${agent.availability}` }),
      el(
        'span',
        { class: 'body' },
        el('b', { text: agent.label }),
        el('span', {
          text: ready ? agent.modelLabel : (agent.detail ?? REASON[agent.availability]),
        }),
      ),
      when(
        () => store.activeAgentName() === agent.name,
        () => el('span', { class: 'trail' }, icon('check', 14)),
      ),
    );
  };

  return el(
    'div',
    { class: 'popover', role: 'menu' },
    el('div', { class: 'menu-head', text: 'Agent' }),
    each(
      () => store.agents(),
      (agent) => agent.name,
      row,
    ),
    el('div', { class: 'menu-sep' }),
    el(
      'button',
      {
        class: 'menu-item',
        type: 'button',
        onClick: () => {
          ctx.send({ type: 'settings.open' });
          ui.closeOverlay();
        },
      },
      icon('settings', 15),
      el('span', { class: 'body' }, el('b', { text: 'AI settings…' })),
    ),
  );
}
