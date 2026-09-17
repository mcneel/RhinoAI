// "May I?" as a card in the chat, rather than a dialog over it.
//
// A tool set to Ask blocks the agent's turn until this is answered, so the card sits at the end of the
// transcript, where the user is already reading, and the host withdraws it however it ends: allowed,
// refused, or the turn cancelled under it.

import { each, el, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import type { PermissionAsk } from '../protocol/events.js';
import { t } from '../i18n/t.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

export function permissionAsks(ctx: PanelContext): Child {
  const card = (ask: PermissionAsk): Child => {
    // Per card, and read only when a button is pressed, so ticking it redraws nothing.
    let remember = false;
    const answer = (allow: boolean): void => ctx.send({ type: 'permission.answer', id: ask.id, allow, remember });

    return el(
      'div',
      { class: 'permission-card', role: 'group', 'aria-label': `Permission: ${ask.title}` },
      el(
        'div',
        { class: 'permission-card-head' },
        el('span', { class: 'glyph' }, icon('question', 15)),
        el('b', { text: `Allow ${ask.title}?` }),
        el('code', { class: 'inl', text: ask.tool }),
      ),
      when(
        () => ask.detail.length > 0,
        () => el('pre', { class: 'permission-card-detail', text: ask.detail }),
      ),
      el(
        'label',
        {
          class: 'permission-card-remember',
          // Says what each answer becomes, and that neither is a one-way door.
          title:
            'Allow: this tool stops asking and just runs. No: it is switched off for this assistant. '
            + 'You can undo either one in the settings dialog, under Permissions.',
        },
        el('input', {
          type: 'checkbox',
          onChange: (event: Event) => {
            remember = (event.target as HTMLInputElement).checked;
          },
        }),
        el('span', { text: () => t('permissions.remember') }),
      ),
      el(
        'div',
        { class: 'permission-card-actions' },
        el('button', { class: 'btn', type: 'button', onClick: () => answer(false) }, () => t('permissions.refuse')),
        el('button', { class: 'btn primary', type: 'button', onClick: () => answer(true) }, () => t('permissions.allow')),
      ),
    );
  };

  return each(
    () => ctx.store.permissionAsks(),
    (ask) => ask.id,
    card,
  );
}
