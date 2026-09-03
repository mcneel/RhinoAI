import { bind, each, el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import type { Notice } from '../state/store.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

const GLYPH = { info: 'bolt', warn: 'alert', error: 'alert' } as const;

export function notices(ctx: PanelContext): Child {
  const row = (notice: Notice): Child => {
    // Errors stay until dismissed; anything lighter clears itself.
    if (notice.level !== 'error') {
      const timer = setTimeout(() => ctx.store.dismissNotice(notice.id), 6000);
      bind(() => () => clearTimeout(timer));
    }
    return el(
      'div',
      { class: `notice ${notice.level}`, role: notice.level === 'error' ? 'alert' : 'status' },
      icon(GLYPH[notice.level], 14),
      el('p', { text: notice.text }),
      el(
        'button',
        { class: 'icon-btn', type: 'button', 'aria-label': 'Dismiss', onClick: () => ctx.store.dismissNotice(notice.id) },
        icon('close', 12),
      ),
    );
  };

  return el(
    'div',
    { class: 'notices' },
    each(
      () => ctx.store.notices(),
      (notice) => notice.id,
      row,
    ),
  );
}
