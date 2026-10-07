import { el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { t } from '../i18n/t.js';
import { agentChip } from './agentMenu.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

export function header(ctx: PanelContext): Child {
  const { store, ui } = ctx;

  return el(
    'header',
    { class: 'header' },
    agentChip(ctx),
    el(
      'button',
      {
        class: () => `icon-btn${ui.overlay() === 'history' ? ' on' : ''}`,
        type: 'button',
        title: () => t('header.history'),
        onClick: () => ui.openOverlay('history'),
      },
      icon('history'),
    ),
    el(
      'button',
      {
        class: 'icon-btn',
        type: 'button',
        title: () => t('header.newConversation'),
        disabled: () => store.running(),
        onClick: () => ctx.send({ type: 'conversation.new' }),
      },
      icon('plus'),
    ),
    el(
      'button',
      { class: 'icon-btn', type: 'button', title: () => t('header.settings'), onClick: () => ctx.send({ type: 'settings.open' }) },
      icon('settings'),
    ),
  );
}
