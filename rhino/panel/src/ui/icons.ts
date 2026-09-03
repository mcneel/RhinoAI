// Inline SVG, stroked in currentColor. Nothing to recolor for dark mode, nothing to rasterize,
// nothing to cache: the Eto panel's LoadIcon / IconCache / HexColor machinery has no counterpart.

import { svg } from '../core/dom.js';

type Shape =
  | { path: string }
  | { circle: readonly [number, number, number]; fill?: boolean }
  | { line: readonly [number, number, number, number] }
  | { rect: readonly [number, number, number, number, number]; fill?: boolean };

const gear = (): Shape[] => {
  const shapes: Shape[] = [{ circle: [12, 12, 3.4] }];
  for (let i = 0; i < 8; i++) {
    const angle = (i * Math.PI) / 4;
    const cos = Math.cos(angle);
    const sin = Math.sin(angle);
    shapes.push({
      line: [12 + cos * 5.6, 12 + sin * 5.6, 12 + cos * 8.4, 12 + sin * 8.4],
    });
  }
  return shapes;
};

const ICONS = {
  terminal: [{ rect: [3, 5, 18, 14, 2.5] }, { path: 'M7.5 10l2.5 2.5-2.5 2.5' }, { line: [13, 15, 17, 15] }],
  document: [{ path: 'M13.5 3H7a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V7.5z' }, { path: 'M13.5 3v4.5H18' }],
  cube: [{ path: 'M12 3l8 4.5v9L12 21l-8-4.5v-9z' }, { path: 'M4 7.5l8 4.5 8-4.5' }, { line: [12, 12, 12, 21] }],
  camera: [
    { path: 'M4 8h3l1.5-2h7L17 8h3a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1z' },
    { circle: [12, 13.5, 3.2] },
  ],
  graph: [{ rect: [3, 4, 6, 4.5, 1.2] }, { rect: [15, 15.5, 6, 4.5, 1.2] }, { path: 'M9 6.2h3.5v11.5H15' }],
  question: [
    { circle: [12, 12, 9] },
    { path: 'M9.6 9.4a2.5 2.5 0 1 1 3.5 2.3c-.9.4-1.1 1-1.1 1.8' },
    { line: [12, 16.6, 12, 16.7] },
  ],
  tool: gear(),
  send: [{ line: [12, 19.5, 12, 5.5] }, { path: 'M5.5 12l6.5-6.5 6.5 6.5' }],
  stop: [{ rect: [7, 7, 10, 10, 2], fill: true }],
  plus: [{ line: [12, 5, 12, 19] }, { line: [5, 12, 19, 12] }],
  minus: [{ line: [5, 12, 19, 12] }],
  paperclip: [
    { path: 'M19.5 11.8l-8 8a4.6 4.6 0 0 1-6.5-6.5l8-8a3.1 3.1 0 0 1 4.3 4.3l-8 8a1.5 1.5 0 0 1-2.2-2.2l7.2-7.2' },
  ],
  settings: [{ line: [4, 8, 20, 8] }, { circle: [9, 8, 2.3] }, { line: [4, 16, 20, 16] }, { circle: [15, 16, 2.3] }],
  history: [{ circle: [12, 12, 8.5] }, { path: 'M12 7.5V12l3.6 2.1' }],
  copy: [
    { rect: [9, 9, 11, 11, 2] },
    { path: 'M6 15H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1h9a1 1 0 0 1 1 1v1' },
  ],
  undo: [{ path: 'M4 9.5h9.5a5 5 0 1 1 0 10H8.5' }, { path: 'M8 5.5l-4 4 4 4' }],
  retry: [{ path: 'M20 12a8 8 0 1 1-2.6-5.9' }, { path: 'M20.5 4.5V9h-4.5' }],
  chevron: [{ path: 'M9 6l6 6-6 6' }],
  check: [{ path: 'M5 13l4.5 4.5L19 7' }],
  alert: [{ path: 'M12 4l9 16H3z' }, { line: [12, 10, 12, 15] }, { line: [12, 17.6, 12, 17.7] }],
  close: [{ line: [6.5, 6.5, 17.5, 17.5] }, { line: [17.5, 6.5, 6.5, 17.5] }],
  at: [{ circle: [12, 12, 4 ] }, { path: 'M16 8v5.2a3 3 0 0 0 5.2 2A9 9 0 1 0 17 20.4' }],
  search: [{ circle: [11, 11, 6.5] }, { line: [15.8, 15.8, 20.5, 20.5] }],
  sparkle: [{ path: 'M12 3.5l1.7 4.8 4.8 1.7-4.8 1.7L12 16.5l-1.7-4.8L5.5 10l4.8-1.7z' }, { path: 'M18.5 15.5l.7 2 2 .7-2 .7-.7 2-.7-2-2-.7 2-.7z' }],
  arrowDown: [{ line: [12, 5, 12, 18.5] }, { path: 'M6 12.5l6 6 6-6' }],
  agent: [{ rect: [4, 8, 16, 11.5, 3.5] }, { line: [12, 4, 12, 8] }, { circle: [9.2, 13.5, 1], fill: true }, { circle: [14.8, 13.5, 1], fill: true }],
  layers: [{ path: 'M12 3l9 5-9 5-9-5z' }, { path: 'M3 13.2l9 5 9-5' }],
  reveal: [{ path: 'M2.5 12S6 6.2 12 6.2 21.5 12 21.5 12 18 17.8 12 17.8 2.5 12 2.5 12z' }, { circle: [12, 12, 3] }],
  bolt: [{ path: 'M13.5 3L5.5 14H11l-1 7 8-11h-5.5z' }],
} satisfies Record<string, Shape[]>;

export type IconName = keyof typeof ICONS;

export function icon(name: IconName, size = 16): SVGElement {
  const node = svg('svg', {
    class: 'icon',
    viewBox: '0 0 24 24',
    width: size,
    height: size,
    fill: 'none',
    stroke: 'currentColor',
    'stroke-width': 1.6,
    'stroke-linecap': 'round',
    'stroke-linejoin': 'round',
    'aria-hidden': 'true',
  });

  for (const shape of ICONS[name] as readonly Shape[]) {
    if ('path' in shape) node.appendChild(svg('path', { d: shape.path }));
    else if ('circle' in shape)
      node.appendChild(
        svg('circle', {
          cx: shape.circle[0],
          cy: shape.circle[1],
          r: shape.circle[2],
          ...(shape.fill ? { fill: 'currentColor', stroke: 'none' } : {}),
        }),
      );
    else if ('line' in shape)
      node.appendChild(svg('line', { x1: shape.line[0], y1: shape.line[1], x2: shape.line[2], y2: shape.line[3] }));
    else
      node.appendChild(
        svg('rect', {
          x: shape.rect[0],
          y: shape.rect[1],
          width: shape.rect[2],
          height: shape.rect[3],
          rx: shape.rect[4],
          ...(shape.fill ? { fill: 'currentColor', stroke: 'none' } : {}),
        }),
      );
  }

  return node;
}
