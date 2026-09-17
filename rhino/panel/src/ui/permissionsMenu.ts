// The permissions for the tools the host groups for quick access (today: the Script Editor),
// anchored above the composer like the slash menu so they sit next to the prompt.
//
// The host owns the state: a toggle sends the wanted mode and the rows are rebuilt when the host
// answers with a fresh `permissions` event, so the panel never shows one Rhino did not accept.

import { el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import type { PermissionEntry, PermissionGroup, PermissionMode } from '../protocol/events.js';
import { t } from '../i18n/t.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

export function permissionsMenu(ctx: PanelContext): Child {
  const { store, ui } = ctx;
  const set = (name: string, mode: PermissionMode): void => ctx.send({ type: 'permission.set', name, mode });
  const checked = (event: Event): boolean => (event.target as HTMLInputElement).checked;

  const row = (entry: PermissionEntry): Child => {
    const on = entry.mode !== 'off';
    const ask = entry.mode === 'ask';
    return el(
      'div',
      { class: `permission-row${on ? '' : ' dim'}`, title: entry.description },
      el(
        'label',
        { class: 'permission-on' },
        el('input', {
          type: 'checkbox',
          checked: on,
          'aria-label': `${entry.title} on`,
          onChange: (event: Event) => set(entry.name, checked(event) ? (ask ? 'ask' : 'on') : 'off'),
        }),
        el('b', { text: entry.title }),
      ),
      el(
        'label',
        { class: 'permission-ask-toggle', title: () => t('permissions.askToggle') },
        el('input', {
          type: 'checkbox',
          checked: ask,
          disabled: !on,
          'aria-label': `${entry.title}: ask first`,
          onChange: (event: Event) => set(entry.name, checked(event) ? 'ask' : 'on'),
        }),
        el('span', { text: () => t('permissions.ask') }),
      ),
    );
  };

  const group = (entry: PermissionGroup): Child => [
    el('div', { class: 'menu-head', text: entry.label }),
    ...entry.tools.map(row),
  ];

  return el(
    'div',
    { class: 'permissions-menu', role: 'menu', 'aria-label': () => t('permissions.title') },
    () => {
      const groups = store.permissionGroups();
      return groups.length > 0
        ? groups.map(group)
        : el('div', { class: 'menu-head', text: () => t('permissions.empty') });
    },
    el('div', { class: 'menu-sep' }),
    el(
      'button',
      {
        class: 'menu-item',
        type: 'button',
        onClick: () => {
          ctx.send({ type: 'settings.open', page: 'Permissions' });
          ui.closeOverlay();
        },
      },
      icon('settings', 15),
      el('span', { class: 'body' }, el('b', { text: () => t('permissions.allInSettings') })),
    ),
  );
}
