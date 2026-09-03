import { el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { highlight } from '../core/highlight.js';
import type { ToolPreview } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

/** A tool result rendered as the thing it is, rather than as the JSON it arrived in. */
export function preview(ctx: PanelContext, value: ToolPreview): Child {
  switch (value.kind) {
    case 'image':
      return el(
        'div',
        null,
        el('img', { class: 'pv-image', src: value.dataUrl, alt: value.caption ?? 'Captured viewport' }),
        value.caption ? el('div', { class: 'pv-caption', text: value.caption }) : null,
      );

    case 'code': {
      const pre = el('pre');
      const code = el('code');
      code.appendChild(highlight(value.text, value.language));
      pre.appendChild(code);
      return el('div', { class: 'tool-section' }, el('h4', { text: value.language }), pre);
    }

    case 'objects':
      return el(
        'div',
        { class: 'pv-objects' },
        ...value.items.map((item) =>
          el(
            'button',
            {
              type: 'button',
              title: 'Select and zoom to this object in Rhino',
              onClick: () => ctx.send({ type: 'context.reveal', id: item.id }),
            },
            icon('cube', 13),
            el('span', { text: item.label }),
            item.layer ? el('span', { class: 'layer', text: item.layer }) : null,
          ),
        ),
      );

    case 'graph':
      return el(
        'div',
        { class: 'pv-graph' },
        el('span', { class: 'chip' }, icon('graph', 12), el('span', { text: `${value.wires} wires` })),
        ...value.components.map((name) => el('span', { class: 'chip' }, el('span', { text: name }))),
        value.errors > 0
          ? el('span', { class: 'chip err' }, icon('alert', 12), el('span', { text: `${value.errors} errors` }))
          : null,
        value.warnings > 0
          ? el('span', { class: 'chip warn' }, icon('alert', 12), el('span', { text: `${value.warnings} warnings` }))
          : null,
      );

    case 'table':
      return el(
        'div',
        { class: 'pv-wrap' },
        el(
          'table',
          { class: 'pv-table' },
          el('thead', null, el('tr', null, ...value.columns.map((column) => el('th', { text: column })))),
          el(
            'tbody',
            null,
            ...value.rows.map((row) => el('tr', null, ...row.map((cell) => el('td', { text: cell })))),
          ),
        ),
      );
  }
}
