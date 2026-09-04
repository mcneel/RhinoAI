import { bind, el, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import type { Signal } from '../core/signal.js';
import { highlight } from '../core/highlight.js';
import { formatDuration, prettyJson } from '../state/format.js';
import { familyOf, iconFor } from '../state/tools.js';
import type { ToolCall } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';
import { preview } from './previews.js';

export function toolCard(ctx: PanelContext, call: Signal<ToolCall>): Child {
  const id = call.peek().id;
  const family = familyOf(call.peek().name);

  // A failure is the one thing worth opening unasked, and only the first time.
  let autoOpened = false;
  bind(() => {
    if (call().status === 'failed' && !autoOpened) {
      autoOpened = true;
      if (!ctx.ui.isToolExpanded(id)) ctx.ui.toggleTool(id);
    }
  });

  const expanded = () => ctx.ui.isToolExpanded(id);

  // A captured view is the result, not a detail of it: show it without asking.
  const inlinePreview = () => {
    const current = call().preview;
    return current?.kind === 'image' && !expanded() ? preview(ctx, current) : null;
  };

  const json = (label: string, body: string) =>
    el(
      'div',
      { class: 'tool-section' },
      el('h4', { text: label }),
      el('pre', null, el('code', { ref: (node: HTMLElement) => node.appendChild(highlight(body, 'json')) })),
    );

  return el(
    'div',
    { class: () => `tool ${call().status}` },
    el(
      'button',
      {
        class: 'tool-head',
        type: 'button',
        'aria-expanded': expanded,
        onClick: () => ctx.ui.toggleTool(id),
      },
      () =>
        call().status === 'running'
          ? el('span', { class: 'spinner' })
          : el('span', { class: 'fam' }, icon(iconFor(family), 14)),
      el('span', { class: 'title', text: () => call().title }),
      // Only worth the space when it says something the phrase does not. An unrecognised tool has
      // no phrase, so its title already is the wire name.
      when(
        () => call().name !== call().title,
        () => el('span', { class: 'wire', text: () => call().name }),
      ),
      when(
        () => call().durationMs !== undefined,
        () => el('span', { class: 'dur', text: () => formatDuration(call().durationMs ?? 0) }),
      ),
      el('span', { class: 'chev' }, icon('chevron', 12)),
    ),
    () => {
      const inline = inlinePreview();
      return inline ? el('div', { class: 'tool-inline' }, inline) : null;
    },
    when(expanded, () => {
      const current = call();
      const args = prettyJson(current.args);
      const result = prettyJson(current.result);
      return el(
        'div',
        { class: 'tool-body' },
        current.preview ? preview(ctx, current.preview) : null,
        current.error
          ? el('div', { class: 'tool-error' }, icon('alert', 13), el('span', { text: current.error }))
          : null,
        args ? json('arguments', args) : null,
        result && !current.preview ? json('result', result) : null,
      );
    }),
  );
}
