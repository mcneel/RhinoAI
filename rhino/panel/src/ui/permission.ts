import { el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { t } from '../i18n/t.js';
import type { PendingPermission, PermissionAnswer } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

export function permissionOverlay(ctx: PanelContext): Child {
  return () => {
    const first = ctx.store.permissions()[0];
    return first ? overlay(ctx, first) : null;
  };
}

function hasArgs(args: unknown): boolean {
  return typeof args === 'object' && args !== null && Object.keys(args).length > 0;
}

function overlay(ctx: PanelContext, permission: PendingPermission): Child {
  const reply = (answer: PermissionAnswer) => ctx.send({ type: 'permission.answer', id: permission.id, answer });

  const deny = el('button', { class: 'btn', type: 'button', onClick: () => reply('deny') }, t('permission.deny'));

  const card = el(
    'div',
    {
      class: 'ask permission',
      role: 'alertdialog',
      'aria-modal': 'true',
      'aria-labelledby': 'permission-title',
      tabindex: '-1',
      onKeyDown: (event: KeyboardEvent) => {
        if (event.key === 'Escape') {
          event.preventDefault();
          reply('deny');
        }
      },
    },
    el(
      'div',
      { class: 'ask-head' },
      el('span', { class: 'glyph', 'aria-hidden': 'true' }, icon('alert', 12)),
      el('h4', { id: 'permission-title', text: t('permission.title', permission.tool) }),
    ),
    el('div', { class: 'permission-from', text: t('permission.from', permission.mcp) }),
    hasArgs(permission.args) ? el('pre', { text: JSON.stringify(permission.args, null, 2) }) : null,
    el(
      'div',
      { class: 'ask-row' },
      el('button', { class: 'btn', type: 'button', onClick: () => reply('always') }, t('permission.always')),
      el('span', { class: 'spacer' }),
      deny,
      el('button', { class: 'btn primary', type: 'button', onClick: () => reply('allow') }, t('permission.allow')),
    ),
  );

  requestAnimationFrame(() => deny.focus({ preventScroll: true }));
  return el('div', { class: 'ask-scrim' }, card);
}
