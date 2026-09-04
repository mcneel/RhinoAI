// Builds the panel three ways from one source tree:
//   dist/panel.js + dist/panel.css   loose assets, for a host that serves a folder
//   dist/panel.html                  one self-contained document, for a WebView given a single file
//   dist/artifact.html               the same page as a fragment (no <html>/<head>/<body>)
// Pass --serve for a watching dev server on http://localhost:5173.
import * as esbuild from 'esbuild';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const dist = resolve(root, 'dist');
const serve = process.argv.includes('--serve');
const minify = !serve;

/** Two builds from one tree: the reviewable prototype, and what a yak would actually carry. */
function options(mock) {
  return {
  entryPoints: [resolve(root, 'src/main.ts')],
  bundle: true,
  format: 'iife',
  target: ['es2022'],
  platform: 'browser',
  outdir: dist,
  entryNames: mock ? 'panel' : 'panel.host',
  define: { __MOCK__: JSON.stringify(mock) },
  assetNames: '[name]',
  loader: { '.svg': 'dataurl', '.png': 'dataurl' },
  minify,
  sourcemap: serve ? 'inline' : false,
  logLevel: 'info',
  metafile: true,
  };
}

const TITLE = 'Rhino AI Panel';

function fragment(css, js) {
  return `<title>${TITLE}</title>
<style>
${css}</style>
<div id="root"></div>
<script>
${js}</script>
`;
}

function document_(css, js) {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="light dark">
<title>${TITLE}</title>
<style>
${css}</style>
</head>
<body>
<div id="root"></div>
<script>
${js}</script>
</body>
</html>
`;
}

const kb = (n) => `${(n / 1024).toFixed(1)}kb`;

async function emitHtml(stem, extra = []) {
  const [css, js] = await Promise.all([
    readFile(resolve(dist, `${stem}.css`), 'utf8'),
    readFile(resolve(dist, `${stem}.js`), 'utf8'),
  ]);
  await Promise.all([
    writeFile(resolve(dist, `${stem}.html`), document_(css, js)),
    ...extra.map((name) => writeFile(resolve(dist, name), fragment(css, js))),
  ]);
  console.log(`  ${stem}.html`.padEnd(20) + `${kb(css.length + js.length)} self-contained`);
}

await mkdir(dist, { recursive: true });

if (!serve) {
  await esbuild.build(options(true));
  await emitHtml('panel', ['artifact.html']);
  await esbuild.build(options(false));
  await emitHtml('panel.host');
} else {
  const ctx = await esbuild.context({
    ...options(true),
    plugins: [{
      name: 'emit-html',
      setup(build) {
        build.onEnd(async (r) => { if (!r.errors.length) await emitHtml('panel', ['artifact.html']); });
      },
    }],
  });
  await ctx.watch();
  const { hosts, port } = await ctx.serve({ servedir: dist, port: 5173 });
  console.log(`\n  dev server  http://${hosts[0] ?? 'localhost'}:${port}/panel.html\n`);
}
