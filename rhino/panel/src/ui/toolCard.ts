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

  // A reviewed transcript's calls have all finished, so its chips could only fire at whatever is running now.
  const chips = () => {
    const offered = call().chips;
    if (!offered || offered.length === 0 || ctx.store.readOnly()) return null;
    return el(
      'div',
      { class: 'tool-chips' },
      ...offered.map((chip) =>
        el(
          'button',
          {
            class: chip.style === 'danger' ? 'tool-chip danger' : 'tool-chip',
            type: 'button',
            onClick: () => ctx.send({ type: 'tool.chip', callId: id, chipId: chip.id }),
          },
          chip.icon ? icon(chip.icon, 11) : null,
          el('span', { text: chip.label }),
        ),
      ),
    );
  };

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
    // The expander cannot span the row: a chip inside it would be a button nested in a button.
    el(
      'div',
      { class: 'tool-head' },
      el(
        'button',
        {
          class: 'tool-toggle',
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
      chips,
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
