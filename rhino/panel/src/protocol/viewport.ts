// A stand-in for a real viewport capture, drawn as an SVG data URI so the mock host needs no assets.

function isoPoint(cx: number, cy: number, x: number, y: number, z: number): string {
  return `${(cx + (x - y) * 0.87).toFixed(1)},${(cy + (x + y) * 0.5 - z).toFixed(1)}`;
}

function box(cx: number, cy: number, w: number, d: number, h: number, tint: string): string {
  const p = (x: number, y: number, z: number) => isoPoint(cx, cy, x, y, z);
  const faces = [
    { points: [p(0, 0, h), p(w, 0, h), p(w, d, h), p(0, d, h)], shade: 1 },
    { points: [p(w, 0, h), p(w, d, h), p(w, d, 0), p(w, 0, 0)], shade: 0.66 },
    { points: [p(0, d, h), p(w, d, h), p(w, d, 0), p(0, d, 0)], shade: 0.42 },
  ];
  return faces
    .map(
      (face) =>
        `<polygon points="${face.points.join(' ')}" fill="${tint}" fill-opacity="${face.shade}" stroke="#0d1014" stroke-opacity=".45" stroke-width="1"/>`,
    )
    .join('');
}

export function viewportCapture(): string {
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

  parts.push(box(196, 268, 46, 46, 96, '#c9d4e0'));
  parts.push(box(300, 236, 34, 34, 148, '#b9c8dc'));
  parts.push(box(376, 288, 58, 58, 54, '#d6dee8'));

  parts.push(
    `<g stroke-width="1.6" fill="none">` +
      `<path d="M28 ${height - 26} l26 15" stroke="#e05c5c"/>` +
      `<path d="M28 ${height - 26} l-26 15" stroke="#5ce07a"/>` +
      `<path d="M28 ${height - 26} l0 -28" stroke="#5c9ce0"/></g>`,
  );
  parts.push(
    `<text x="14" y="22" fill="#9aa5b1" font-family="system-ui,sans-serif" font-size="13">Perspective</text>`,
  );

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${width} ${height}" width="${width}" height="${height}">${parts.join('')}</svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}
