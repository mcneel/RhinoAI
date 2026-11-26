import { el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { t } from '../i18n/t.js';
import type { StringKey } from '../i18n/strings.js';
import type { McpInfo, McpKind } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon, type IconName } from './icons.js';

const KIND_LABEL: Record<McpKind, StringKey> = {
  stdio: 'mcp.kindStdio',
  http: 'mcp.kindHttp',
  memory: 'mcp.kindMemory',
};

const KIND_ICON: Record<McpKind, IconName> = {
  stdio: 'terminal',
  http: 'popOut',
  memory: 'tool',
};

function detail(mcp: McpInfo): string {
  const kind = t(KIND_LABEL[mcp.kind]);
  if (mcp.tools === 0) return kind;
  const tools = mcp.tools === 1 ? t('mcp.toolCountOne') : t('mcp.toolCountMany', String(mcp.tools));
  return `${kind} · ${tools}`;
}

export function mcpMenu(ctx: PanelContext): Child {
  const { store, ui } = ctx;

  const toggle = (mcp: McpInfo): void => {
    const enabled = !mcp.enabled;
    store.setMcpEnabled(mcp.name, enabled);
    ctx.send({ type: 'mcp.toggle', name: mcp.name, enabled });
  };

  const row = (mcp: McpInfo): Child =>
    el(
      'button',
      {
        class: `menu-item${mcp.enabled ? '' : ' dim'}`,
        type: 'button',
        role: 'menuitemcheckbox',
        'aria-checked': String(mcp.enabled),
        disabled: !mcp.canToggle,
        onClick: () => toggle(mcp),
      },
      icon(KIND_ICON[mcp.kind], 14),
      el('span', { class: 'body' }, el('b', { text: mcp.name }), el('span', { text: () => detail(mcp) })),
      el('span', { class: `switch${mcp.enabled ? ' on' : ''}`, 'aria-hidden': 'true' }),
    );

  const body = (): Child => {
    const mcps = store.mcps();
    if (mcps === null) return el('div', { class: 'menu-note', text: () => t('mcp.loading') });
    if (mcps.length === 0) return el('div', { class: 'menu-note', text: () => t('mcp.none') });
    return mcps.map(row);
  };

  return [
    el('div', { class: 'menu-catcher', onClick: () => ui.closeOverlay() }),
    el(
      'div',
      { class: 'mention-menu mcp-menu', role: 'menu' },
      el('div', { class: 'menu-head', text: () => t('mcp.menuHead') }),
      body,
    ),
  ];
}
