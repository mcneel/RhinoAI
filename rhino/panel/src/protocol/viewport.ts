// Stand-ins for a real viewport capture, drawn as SVG data URIs so the mock host needs no assets.
//
// The facade scene is counted, not suggested: 160 panels on the tall face and 52 on the lower wing
// are exactly the 212 objects the seeded conversation talks about, and the 12 warm ones wrapping the
// near corner are the doubly curved panels it goes on to tag. The corner scene is the same corner
// after the split, so the two captures in one transcript are not the same picture twice.

export type CaptureScene = 'facade' | 'corner';

const PANEL = '#c9d4e0';
const CURVED = '#e0a15c';
const SPLIT = '#9fc8e0';

// Axonometric from outside the corner and above it — the camera sits in -x,-y,+z — so the two faces
// the transcript talks about are the ones turned toward the viewer, and the ground recedes upward
// the way the grid behind it does. Both horizontal axes therefore climb the screen; only a camera
// under the ground plane would send them down.
function isoPoint(cx: number, cy: number, x: number, y: number, z: number): string {
  return `${(cx + (x - y) * 0.87).toFixed(1)},${(cy - (x + y) * 0.5 - z).toFixed(1)}`;
}

type Corner = readonly [number, number, number];

function quad(cx: number, cy: number, corners: readonly Corner[], tint: string, shade: number): string {
  const points = corners.map(([x, y, z]) => isoPoint(cx, cy, x, y, z)).join(' ');
  return `<polygon points="${points}" fill="${tint}" fill-opacity="${shade}" stroke="#0d1014" stroke-opacity=".45" stroke-width=".8"/>`;
}

// The seam a split panel now carries, corner to corner of the quad.
function seam(cx: number, cy: number, corners: readonly Corner[]): string {
  const from = isoPoint(cx, cy, ...corners[0]!);
  const to = isoPoint(cx, cy, ...corners[2]!);
  return `<path d="M${from} L${to}" stroke="#0d1014" stroke-opacity=".55" stroke-width="1" fill="none"/>`;
}

// A panel on the tall face, which faces the light, and one on the wing, which does not.
const litShade = 0.86;
const shyShade = 0.52;

function onTallFace(x: number, z: number, dx: number, dz: number): Corner[] {
  return [
    [x, 0, z + dz],
    [x + dx, 0, z + dz],
    [x + dx, 0, z],
    [x, 0, z],
  ];
}

function onWing(y: number, z: number, dy: number, dz: number): Corner[] {
  return [
    [0, y, z + dz],
    [0, y + dy, z + dz],
    [0, y + dy, z],
    [0, y, z],
  ];
}

// 10 x 16 on the tall face, 4 x 13 on the setback wing: 212 panels, of which the 12 in the corner
// column across the wing's top six courses are the ones that twist.
function facade(): string {
  const cx = 212;
  const cy = 336;
  const dx = 26;
  const dy = 24;
  const dz = 10;
  const twists = (column: number, row: number) => column === 0 && row >= 7 && row <= 12;
  const parts: string[] = [];

  for (let column = 0; column < 10; column++)
    for (let row = 0; row < 16; row++)
      parts.push(quad(cx, cy, onTallFace(column * dx, row * dz, dx, dz),
        twists(column, row) ? CURVED : PANEL, litShade));

  for (let column = 0; column < 4; column++)
    for (let row = 0; row < 13; row++)
      parts.push(quad(cx, cy, onWing(column * dy, row * dz, dy, dz),
        twists(column, row) ? CURVED : PANEL, shyShade));

  return parts.join('');
}

// The same corner close up, after the four panels either side of it became eight triangles.
function corner(): string {
  const cx = 250;
  const cy = 298;
  const dx = 68;
  const dy = 62;
  const dz = 44;
  const wasSplit = (column: number, row: number) => column === 0 && row >= 1;
  const parts: string[] = [];

  for (let column = 0; column < 3; column++)
    for (let row = 0; row < 3; row++) {
      const face = onTallFace(column * dx, row * dz, dx, dz);
      parts.push(quad(cx, cy, face, wasSplit(column, row) ? SPLIT : PANEL, litShade));
      if (wasSplit(column, row)) parts.push(seam(cx, cy, face));
    }

  for (let column = 0; column < 2; column++)
    for (let row = 0; row < 3; row++) {
      const face = onWing(column * dy, row * dz, dy, dz);
      parts.push(quad(cx, cy, face, wasSplit(column, row) ? SPLIT : PANEL, shyShade));
      if (wasSplit(column, row)) parts.push(seam(cx, cy, face));
    }

  return parts.join('');
}

export function viewportCapture(scene: CaptureScene = 'facade'): string {
  const width = 560;
  const height = 340;
  const vpX = 288;
  const vpY = 118;
  const parts: string[] = [];

  parts.push(
    `<defs><linearGradient id="sky" x1="0" y1="0" x2="0" y2="1">` +
      `<stop offset="0" stop-color="#2f353d"/><stop offset="1" stop-color="#1b1f24"/></linearGradient></defs>`,
  );
  parts.push(`<rect width="${width}" height="${height}" fill="url(#sky)"/>`);

  const grid: string[] = [];
  for (let i = 0; i <= 24; i++) {
    const x = -560 + (i * (width + 1120)) / 24;
    grid.push(`M${x} ${height} L${vpX} ${vpY}`);
  }
  for (let i = 1; i <= 16; i++) {
    const y = vpY + (height - vpY) * (i / 16) ** 2.1;
    grid.push(`M0 ${y.toFixed(1)} L${width} ${y.toFixed(1)}`);
  }
  parts.push(`<path d="${grid.join(' ')}" stroke="#485360" stroke-opacity=".5" stroke-width=".7" fill="none"/>`);
  parts.push(`<path d="M0 ${vpY} L${width} ${vpY}" stroke="#5b6775" stroke-width="1" fill="none"/>`);

  parts.push(scene === 'corner' ? corner() : facade());

  parts.push(
    `<g stroke-width="1.6" fill="none">` +
      `<path d="M28 ${height - 26} l26 -15" stroke="#e05c5c"/>` +
      `<path d="M28 ${height - 26} l-26 -15" stroke="#5ce07a"/>` +
      `<path d="M28 ${height - 26} l0 -28" stroke="#5c9ce0"/></g>`,
  );
  parts.push(
    `<text x="14" y="22" fill="#9aa5b1" font-family="system-ui,sans-serif" font-size="13">Perspective</text>`,
  );

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${width} ${height}" width="${width}" height="${height}">${parts.join('')}</svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}
