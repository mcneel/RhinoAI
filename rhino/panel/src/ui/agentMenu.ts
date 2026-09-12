import { each, el, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { t } from '../i18n/t.js';
import type { StringKey } from '../i18n/strings.js';
import type { AgentAvailability, AgentInfo } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

const REASON: Record<AgentAvailability, StringKey | null> = {
  ready: null,
  disabled: 'agent.reasonDisabled',
  missing: 'agent.reasonMissing',
  signin: 'agent.reasonSignin',
};

function reasonFor(agent: AgentInfo): string {
  const key = REASON[agent.availability];
  return agent.detail ?? (key === null ? '' : t(key));
}

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
        title: () => (ready ? t('agent.useNamed', agent.label) : reasonFor(agent)),
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
          text: () => (ready ? agent.modelLabel : reasonFor(agent)),
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
    el('div', { class: 'menu-head', text: () => t('agent.menuHead') }),
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
      el('span', { class: 'body' }, el('b', { text: () => t('agent.settings') })),
    ),
  );
}
