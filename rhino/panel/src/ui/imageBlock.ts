import { el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { signal } from '../core/signal.js';
import { t } from '../i18n/t.js';
import { formatBytes, imageLabel } from '../state/format.js';
import type { TurnImage } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

/** An image the agent wrote to disk, drawn at the width of the transcript. */
export function imageBlock(ctx: PanelContext, image: TurnImage): Child {
  const open = () => ctx.send({ type: 'image.open', id: image.id });
  const pixels = signal('');

  const picture = el('img', {
    class: 'img-block-picture',
    src: image.src,
    alt: image.name,
    loading: 'lazy',
    onDblClick: open,
    onLoad: () => pixels.set(`${picture.naturalWidth} × ${picture.naturalHeight}`),
    // Nothing worth drawing for a file the host can no longer serve: the caption still names it.
    onError: () => {
      frame.hidden = true;
    },
  });

  const frame = el(
    'div',
    { class: 'img-frame' },
    picture,
    el(
      'button',
      { type: 'button', class: 'img-open', title: t('image.openNamed', image.name), onClick: open },
      icon('popOut', 13),
    ),
  );

  return el(
    'figure',
    { class: 'img-block' },
    frame,
    el(
      'figcaption',
      null,
      el('span', { class: 'name', title: image.name, text: imageLabel(image.name) }),
      el('span', { class: 'size', text: () => [pixels(), formatBytes(image.bytes)].filter(Boolean).join(' · ') }),
      el(
        'button',
        {
          type: 'button',
          class: 'img-save',
          title: t('image.saveNamed', image.name),
          onClick: () => ctx.send({ type: 'image.save', id: image.id }),
        },
        icon('download', 13),
        el('span', { text: t('image.save') }),
      ),
    ),
  );
}
