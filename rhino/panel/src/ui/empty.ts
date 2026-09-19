import { el, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { t } from '../i18n/t.js';
import type { StringKey } from '../i18n/strings.js';
import type { PanelContext } from './context.js';
import { icon, type IconName } from './icons.js';

const STARTERS: readonly { icon: IconName; key: StringKey }[] = [
  { icon: 'terminal', key: 'empty.starterCommand' },
  { icon: 'graph', key: 'empty.starterTower' },
  { icon: 'layers', key: 'empty.starterLayers' },
];

export function emptyState(ctx: PanelContext): Child {
  return when(
    () => ctx.store.hasReadyAgent(),
    () =>
      el(
        'div',
        { class: 'empty' },
        el(
          'div',
          { class: 'empty-title' },
          icon('sparkle', 17),
          el('span', { text: () => t('empty.ready', ctx.store.activeAgent()?.label ?? t('empty.theAgent')) }),
        ),
        el('p', { text: () => t('empty.body') }),
        el(
          'div',
          { class: 'starters' },
          ...STARTERS.map((starter) =>
            el(
              'button',
              { class: 'starter', type: 'button', onClick: () => ctx.submit(t(starter.key)) },
              icon(starter.icon, 15),
              el('span', { text: () => t(starter.key) }),
            ),
          ),
        ),
      ),
    () =>
      el(
        'div',
        { class: 'empty' },
        el('div', { class: 'empty-title' }, icon('agent', 17), el('span', { text: () => t('empty.noAgent') })),
        el('p', { text: () => t('empty.noAgentBody') }),
        el(
          'div',
          { class: 'starters' },
          el(
            'button',
            { class: 'starter', type: 'button', onClick: () => ctx.send({ type: 'settings.open' }) },
            icon('settings', 15),
            el('span', { text: () => t('empty.openSettings') }),
          ),
          el(
            'button',
            {
              class: 'starter',
              type: 'button',
              onClick: () => ctx.openLink('https://mcneel.github.io/RhinoAI/'),
            },
            icon('reveal', 15),
            el('span', { text: () => t('empty.setupGuide') }),
          ),
        ),
      ),
  );
}
