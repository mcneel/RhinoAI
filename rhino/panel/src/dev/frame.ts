// Review chrome, mock host only: a resizable stage so a docked-panel width can actually be judged,
// plus a theme switch. None of this ships inside Rhino.

import { el } from '../core/dom.js';
import { signal } from '../core/signal.js';
import type { Store } from '../state/store.js';

const PRESETS = [
  { label: 'Narrow', width: 264 },
  { label: 'Docked', width: 340 },
  { label: 'Wide', width: 460 },
] as const;

export function devFrame(root: HTMLElement, store: Store): HTMLElement {
  const width = signal(340);
  const full = signal(false);

  const stage = el('div', { class: 'devstage' });
  const panelHost = el('div', {
    class: 'devpanel',
    style: { width: () => (full() ? '100%' : `${width()}px`), 'max-width': '100%' },
  });

  const grip = el('div', {
    class: 'devgrip',
    title: 'Drag to resize',
    onPointerDown: (event: PointerEvent) => {
      if (full()) return;
      const startX = event.clientX;
      const startWidth = width();
      (event.target as HTMLElement).setPointerCapture(event.pointerId);
      const move = (moveEvent: PointerEvent) =>
        width.set(Math.max(200, Math.min(900, startWidth + (moveEvent.clientX - startX))));
      const up = () => {
        window.removeEventListener('pointermove', move);
        window.removeEventListener('pointerup', up);
      };
      window.addEventListener('pointermove', move);
      window.addEventListener('pointerup', up);
    },
  });
  panelHost.appendChild(grip);
  stage.appendChild(panelHost);

  const presetButton = (label: string, apply: () => void, active: () => boolean) =>
    el('button', { type: 'button', class: () => (active() ? 'on' : ''), onClick: apply }, label);

  const bar = el(
    'div',
    { class: 'devbar' },
    el('span', { class: 'brand', text: 'Rhino AI' }),
    el('span', { class: 'tag', text: 'prototype' }),
    el('span', { class: 'spacer' }),
    ...PRESETS.map((preset) =>
      presetButton(
        preset.label,
        () => {
          full.set(false);
          width.set(preset.width);
        },
        () => !full() && width() === preset.width,
      ),
    ),
    presetButton('Full', () => full.set(true), () => full()),
    el('span', {
      class: 'devwidth',
      text: () => (full() ? 'full width' : `${width()} px`),
    }),
    presetButton(
      'Light / dark',
      () => store.scheme.set((current) => (current === 'dark' ? 'light' : 'dark')),
      () => false,
    ),
  );

  root.classList.add('devframe');
  root.appendChild(bar);
  root.appendChild(stage);
  return panelHost;
}
