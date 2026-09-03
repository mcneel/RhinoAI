// Black-box checks against the built panel in a real browser: markdown, highlighting, layout at
// docked widths, streaming, and the one architectural claim worth asserting - that an earlier turn
// is never rebuilt when a later one streams.
//
// Uses the locally installed Chrome via puppeteer-core. Skips (exit 0) when no browser is found, so
// it is safe in an environment without one. Pass --shots to also write dist/shots/*.png.

import { existsSync, mkdirSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const page_url = `file://${resolve(root, 'dist/panel.html')}`;
const shots = process.argv.includes('--shots');
const shotDir = resolve(root, 'dist/shots');

const CANDIDATES = [
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/Applications/Chromium.app/Contents/MacOS/Chromium',
  '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium',
];

const executablePath = process.env.CHROME_PATH ?? CANDIDATES.find((p) => existsSync(p));
if (!executablePath) {
  console.log('verify: no Chrome/Edge found, skipping browser checks');
  process.exit(0);
}
if (!existsSync(resolve(root, 'dist/panel.html'))) {
  console.error('verify: dist/panel.html is missing, run `npm run build` first');
  process.exit(1);
}

let puppeteer;
try {
  puppeteer = (await import('puppeteer-core')).default;
} catch {
  console.log('verify: puppeteer-core is not installed, skipping browser checks');
  process.exit(0);
}

const failures = [];
const check = (name, ok, detail = '') => {
  if (ok) console.log(`  ok    ${name}`);
  else {
    console.log(`  FAIL  ${name}${detail ? ` - ${detail}` : ''}`);
    failures.push(name);
  }
};

const wait = (ms) => new Promise((r) => setTimeout(r, ms));

/** Polls a page expression or function; returns the first truthy result, or null on timeout. */
async function until(page, fnOrExpression, timeoutMs = 4000, everyMs = 60) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const value = await page.evaluate(fnOrExpression);
    if (value) return value;
    if (Date.now() > deadline) return null;
    await wait(everyMs);
  }
}

const browser = await puppeteer.launch({
  executablePath,
  headless: true,
  timeout: 30000,
  args: ['--no-sandbox', '--disable-gpu', '--force-color-profile=srgb'],
});

try {
  const page = await browser.newPage();
  page.setDefaultTimeout(8000);
  await page.setViewport({ width: 1180, height: 880, deviceScaleFactor: shots ? 2 : 1 });

  const consoleProblems = [];
  page.on('console', (m) => { if (m.type() === 'error') consoleProblems.push(m.text()); });
  page.on('pageerror', (e) => consoleProblems.push(String(e.message)));
  page.on('error', (e) => consoleProblems.push(`crash: ${e.message}`));

  if (shots) mkdirSync(shotDir, { recursive: true });
  const shot = async (name) => { if (shots) await page.screenshot({ path: resolve(shotDir, `${name}.png`) }); };

  await page.goto(page_url, { waitUntil: 'domcontentloaded' });
  await wait(800);
  await shot('01-seeded');

  const type = async (text) => {
    await page.click('.composer textarea');
    await page.type('.composer textarea', text, { delay: 2 });
  };

  // ---------------------------------------------------------------- markdown
  const md = await page.evaluate(() => {
    const q = (s) => document.querySelectorAll(s).length;
    return {
      strong: q('.msg-agent strong'),
      inlineCode: q('.msg-agent p code'),
      bullets: q('.msg-agent ul li'),
      quote: q('.msg-agent blockquote'),
      tableRows: q('.msg-agent .md-table tbody tr'),
      tableHeads: q('.msg-agent .md-table th'),
      rawPipes: [...document.querySelectorAll('.msg-agent p')].filter((p) => p.textContent.includes('| ---')).length,
      strayMarkers: [...document.querySelectorAll('.msg-agent')].filter((m) => /\*\*|`{1,3}/.test(m.textContent)).length,
    };
  });
  check('markdown renders bold', md.strong > 0);
  check('markdown renders inline code', md.inlineCode > 0);
  check('markdown renders bullet lists', md.bullets >= 4);
  check('markdown renders blockquotes', md.quote > 0);
  check('markdown renders tables', md.tableRows >= 3 && md.tableHeads === 3, JSON.stringify(md));
  check('table syntax is not left as a paragraph', md.rawPipes === 0);
  check('no unconsumed markdown markers leak into text', md.strayMarkers === 0);

  // ------------------------------------------------------- tool cards / json
  await page.click('.tool-head');
  await wait(200);
  const json = await page.evaluate(() => ({
    keys: document.querySelectorAll('.tool-body .tok-attr').length,
    strings: document.querySelectorAll('.tool-body .tok-str').length,
    preview: document.querySelectorAll('.tool-body .pv-table tbody tr').length,
  }));
  check('tool arguments are highlighted as json', json.keys >= 2 && json.strings >= 1, JSON.stringify(json));
  check('a table result renders as a table, not json', json.preview >= 3);
  await shot('02-tool-expanded');
  await page.click('.tool-head');

  // ------------------------------------------------ streaming + reconciliation
  await page.evaluate(() => {
    // Tag every existing turn so we can tell a survivor from a rebuild.
    document.querySelectorAll('.turn').forEach((t, i) => { t.dataset.probe = `probe-${i}`; });
  });
  const before = await page.evaluate(() => document.querySelectorAll('.turn').length);

  await type('Build a parametric facade in Grasshopper');
  await page.keyboard.press('Enter');

  // Catch the caret while the first assistant run is still arriving, and tag that exact element so
  // the next sample proves the delta grew it rather than replacing it.
  const streaming = await until(page, () => {
    const block = document.querySelector('.msg-agent.streaming');
    if (!block) return null;
    block.dataset.probe = 'streaming-block';
    return { length: block.textContent.length };
  });
  check('the live block shows a streaming caret', streaming !== null);

  const baseline = streaming?.length ?? 0;
  const grown = await until(
    page,
    `(() => {
       const block = document.querySelector('.msg-agent[data-probe="streaming-block"]');
       if (!block) return null;
       const length = block.textContent.length;
       return length > ${baseline} ? { length } : null;
     })()`,
  );
  check('a delta grows the same element in place', grown !== null, `stuck at ${baseline} chars`);

  const mid = await page.evaluate(() => ({
    turns: document.querySelectorAll('.turn').length,
    probesKept: document.querySelectorAll('.turn[data-probe]').length,
    thinking: document.querySelectorAll('.status-strip').length,
    stopButton: document.querySelectorAll('.send.stop').length,
    tools: document.querySelectorAll('.tool').length,
  }));
  check('a new turn is appended', mid.turns === before + 1, `${before} -> ${mid.turns}`);
  check('earlier turns are not rebuilt', mid.probesKept === before, `${mid.probesKept}/${before} survived`);
  check('a status strip reports progress', mid.thinking === 1);
  check('send becomes stop while running', mid.stopButton === 1);
  await shot('03-streaming');

  const later = await until(
    page,
    `(() => {
       const count = document.querySelectorAll('.tool').length;
       return count > ${mid.tools} ? { count } : null;
     })()`,
    8000,
  );
  check('tool calls append as the turn proceeds', later !== null, `stuck at ${mid.tools} cards`);
  check('earlier turns still are not rebuilt mid-turn',
    (await page.evaluate(() => document.querySelectorAll('.turn[data-probe]').length)) === before);

  // Autoscroll runs off a ResizeObserver, which fires after layout, so assert that it settles
  // pinned rather than that it is pinned at one arbitrary instant.
  const pinned = await until(
    page,
    `(() => {
       const t = document.querySelector('.transcript');
       const gap = t.scrollHeight - t.scrollTop - t.clientHeight;
       return gap < 30 ? { gap } : null;
     })()`,
    2000,
  );
  check('the transcript settles pinned to the tail', pinned !== null, 'never reached the bottom');

  // The counterpart: a real gesture must still unpin, or the user cannot read back mid-turn.
  const centre = await page.evaluate(() => {
    const r = document.querySelector('.transcript').getBoundingClientRect();
    return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) };
  });
  await page.mouse.move(centre.x, centre.y);
  await page.mouse.wheel({ deltaY: -400 });
  const unpinned = await until(page, `document.querySelectorAll('.jump button').length === 1 ? { shown: true } : null`, 3000);
  check('scrolling up unpins and offers a jump back', unpinned !== null);

  if (unpinned) {
    await page.click('.jump button');
    const repinned = await until(
      page,
      `(() => {
         const t = document.querySelector('.transcript');
         return document.querySelectorAll('.jump').length === 0 &&
           t.scrollHeight - t.scrollTop - t.clientHeight < 30 ? { back: true } : null;
       })()`,
      3000,
    );
    check('the jump pill returns to the tail and re-pins', repinned !== null);
  }

  // ------------------------------------------------------------------- escape
  await page.click('.composer textarea');
  await page.keyboard.press('Escape');
  await wait(300);
  const cancelled = await page.evaluate(() => ({
    stop: document.querySelectorAll('.send.stop').length,
    cancelledNote: [...document.querySelectorAll('.lifecycle')].some((n) => n.textContent === 'stopped'),
  }));
  check('escape cancels the running turn', cancelled.stop === 0 && cancelled.cancelledNote);

  // ------------------------------------------------------- composer affordances
  await type('/');
  await wait(250);
  const slash = await page.evaluate(() => document.querySelectorAll('.mention-menu .menu-item').length);
  check('slash opens the command menu', slash >= 4, `${slash} commands`);
  await page.keyboard.press('Escape');
  await wait(150);

  await type('@');
  await wait(250);
  const mention = await page.evaluate(() => document.querySelectorAll('.mention-menu .menu-item').length);
  check('at opens the context menu', mention >= 4, `${mention} items`);
  await page.keyboard.press('Enter');
  await wait(250);
  const chip = await page.evaluate(() => ({
    chips: document.querySelectorAll('.composer .chip.accent').length,
    draft: document.querySelector('.composer textarea').value,
  }));
  check('picking context adds a chip and clears the token', chip.chips === 1 && !chip.draft.includes('@'), JSON.stringify(chip));
  await shot('04-context');

  // ------------------------------------------------------------------- layout
  for (const [label, preset] of [['narrow', 'Narrow'], ['docked', 'Docked'], ['wide', 'Wide'], ['full', 'Full']]) {
    await page.evaluate((p) => {
      [...document.querySelectorAll('.devbar button')].find((b) => b.textContent === p)?.click();
    }, preset);
    await wait(350);
    const overflow = await page.evaluate(() => {
      const t = document.querySelector('.transcript');
      const c = document.querySelector('.composer');
      return {
        transcript: t.scrollWidth - t.clientWidth,
        composer: c.scrollWidth - c.clientWidth,
        width: Math.round(document.querySelector('.panel').getBoundingClientRect().width),
      };
    });
    check(`no horizontal overflow at ${label} (${overflow.width}px)`, overflow.transcript <= 1 && overflow.composer <= 1, JSON.stringify(overflow));
  }
  await page.evaluate(() => {
    [...document.querySelectorAll('.devbar button')].find((b) => b.textContent === 'Docked')?.click();
  });

  // -------------------------------------------------------------- question card
  await page.evaluate(() => {
    [...document.querySelectorAll('.header .icon-btn')][1].click();
  });
  await wait(300);
  const empty = await page.evaluate(() => ({
    starters: document.querySelectorAll('.starter').length,
    turns: document.querySelectorAll('.turn').length,
  }));
  check('a new conversation shows the empty state', empty.turns === 0 && empty.starters === 3, JSON.stringify(empty));

  await type('audit the selected objects');
  await page.keyboard.press('Enter');
  await wait(9000);
  const question = await page.evaluate(() => ({
    cards: document.querySelectorAll('.question').length,
    options: document.querySelectorAll('.question input[type="radio"]').length,
    other: document.querySelectorAll('.question input[type="text"]').length,
    answerDisabled: document.querySelector('.question .btn.primary')?.disabled,
    failedCard: document.querySelectorAll('.tool.failed').length,
    failedOpen: document.querySelectorAll('.tool.failed .tool-error').length,
    thinking: document.querySelectorAll('.status-strip').length,
  }));
  check('a pending question renders as a form', question.cards === 1 && question.options === 3 && question.other === 1, JSON.stringify(question));
  check('answer stays disabled until something is chosen', question.answerDisabled === true);
  check('a failed tool call auto-expands its error', question.failedCard === 1 && question.failedOpen === 1);
  check('no thinking cue while blocked on the question', question.thinking === 0);
  await shot('05-question');

  await page.evaluate(() => {
    document.querySelectorAll('.question label')[0].click();
  });
  await wait(150);
  await page.evaluate(() => document.querySelector('.question .btn.primary').click());
  await wait(3500);
  const answered = await page.evaluate(() => ({
    cards: document.querySelectorAll('.question').length,
    tools: document.querySelectorAll('.tool').length,
  }));
  check('answering clears the card and the turn continues', answered.cards === 0 && answered.tools >= 3, JSON.stringify(answered));
  await shot('06-answered');

  // ------------------------------------------------------------------- history
  await page.evaluate(() => [...document.querySelectorAll('.header .icon-btn')][0].click());
  await wait(300);
  const history = await page.evaluate(() => document.querySelectorAll('.convo').length);
  check('history lists saved conversations', history === 3, `${history} rows`);
  await page.type('.drawer input', 'renaming');
  await wait(250);
  const filtered = await page.evaluate(() => document.querySelectorAll('.convo').length);
  check('history search filters', filtered === 1, `${filtered} rows`);
  await page.evaluate(() => document.querySelector('.convo').click());
  await wait(400);
  const review = await page.evaluate(() => ({
    reviewBar: document.querySelectorAll('.review-bar').length,
    composer: document.querySelectorAll('.composer').length,
  }));
  check('loading a saved conversation swaps the composer for a review bar', review.reviewBar === 1 && review.composer === 0);
  await shot('07-review');

  // -------------------------------------------------- too-old engine fallback
  const old = await browser.newPage();
  old.setDefaultTimeout(6000);
  await old.setViewport({ width: 900, height: 700 });
  await old.evaluateOnNewDocument(() => {
    const real = CSS.supports.bind(CSS);
    CSS.supports = (...args) =>
      String(args[0]).includes('container-type') ? false : real(...args);
  });
  await old.goto(page_url, { waitUntil: 'domcontentloaded' });
  await wait(500);
  const gate = await old.evaluate(() => ({
    notice: document.querySelectorAll('.unsupported').length,
    panel: document.querySelectorAll('.panel').length,
    override: document.querySelectorAll('.unsupported .btn').length,
  }));
  check('an unsupported engine gets a notice, not a broken panel',
    gate.notice === 1 && gate.panel === 0 && gate.override === 1, JSON.stringify(gate));

  await old.click('.unsupported .btn');
  await wait(700);
  const overridden = await old.evaluate(() => ({
    notice: document.querySelectorAll('.unsupported').length,
    panel: document.querySelectorAll('.panel').length,
    turns: document.querySelectorAll('.turn').length,
  }));
  check('"show it anyway" starts the panel', overridden.notice === 0 && overridden.panel === 1 && overridden.turns > 0,
    JSON.stringify(overridden));
  await old.close();

  check('no css color-mix left in the shipped stylesheet',
    !(await page.evaluate(() => [...document.styleSheets]
      .flatMap((sheet) => { try { return [...sheet.cssRules]; } catch { return []; } })
      .some((rule) => rule.cssText.includes('color-mix')))));

  // --------------------------------------------------------- notices / status
  // The review check above left the panel read-only, which has no composer.
  await page.evaluate(() => document.querySelector('.review-bar .btn')?.click());
  await wait(400);
  await page.evaluate(() => [...document.querySelectorAll('.header .icon-btn')][2].click());
  await wait(300);
  const placement = await page.evaluate(() => {
    const notice = document.querySelector('.notice');
    const header = document.querySelector('.header');
    const transcript = document.querySelector('.transcript');
    const composer = document.querySelector('.composer');
    if (!notice || !header || !transcript) return null;
    return {
      belowHeader: notice.getBoundingClientRect().top >= header.getBoundingClientRect().bottom,
      nearTop: notice.getBoundingClientRect().top < transcript.getBoundingClientRect().top + 60,
      hasComposer: composer !== null,
    };
  });
  check('a notice sits just under the header', placement !== null && placement.belowHeader && placement.nearTop,
    JSON.stringify(placement));

  const cleared = await until(page, `document.querySelectorAll('.notice').length === 0 ? { gone: true } : null`, 7000);
  check('a notice clears itself without being dismissed', cleared !== null);

  await type('Build a parametric facade in Grasshopper');
  await page.keyboard.press('Enter');
  const strip = await until(
    page,
    `(() => {
       const s = document.querySelector('.status-strip');
       const t = document.querySelector('.transcript');
       return s ? { belowTranscript: s.getBoundingClientRect().top >= t.getBoundingClientRect().bottom - 1 } : null;
     })()`,
  );
  check('the status strip sits below the transcript', strip?.belowTranscript === true, JSON.stringify(strip));
  await page.click('.composer textarea');
  await page.keyboard.press('Escape');
  await wait(300);

  // -------------------------------------------------------- zoom / right click
  const zoomOf = () => page.evaluate(() => document.querySelector('.panel').style.zoom || '1');

  // Without a host there is nothing to draw a native menu, so the browser must keep its own.
  const browserMenuKept = await page.evaluate(() => {
    let prevented = null;
    const probe = (e) => { prevented = e.defaultPrevented; };
    window.addEventListener('contextmenu', probe);
    document.querySelector('.transcript').dispatchEvent(
      new MouseEvent('contextmenu', { bubbles: true, cancelable: true }),
    );
    window.removeEventListener('contextmenu', probe);
    return { prevented, inPageMenus: document.querySelectorAll('.ctx-menu').length };
  });
  check('with no host, right click leaves the browser menu alone',
    browserMenuKept.prevented === false && browserMenuKept.inPageMenus === 0, JSON.stringify(browserMenuKept));

  await page.keyboard.down(process.platform === 'darwin' ? 'Meta' : 'Control');
  await page.keyboard.press('Equal');
  await page.keyboard.up(process.platform === 'darwin' ? 'Meta' : 'Control');
  await wait(150);
  // Level 1 is the design's natural size, which is CSS zoom 0.9; one rung in is 1.1 x 0.9.
  check('the keyboard shortcut zooms in', (await zoomOf()) === '0.99', await zoomOf());

  await page.keyboard.down(process.platform === 'darwin' ? 'Meta' : 'Control');
  await page.keyboard.press('Digit0');
  await page.keyboard.up(process.platform === 'darwin' ? 'Meta' : 'Control');
  await wait(150);
  check('the reset shortcut returns to 100%', (await zoomOf()) === '0.9', await zoomOf());

  const panelCentre = await page.evaluate(() => {
    const r = document.querySelector('.panel').getBoundingClientRect();
    return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 3) };
  });
  await page.mouse.move(panelCentre.x, panelCentre.y);
  await page.keyboard.down('Control');
  await page.mouse.wheel({ deltaY: -120 });
  await page.keyboard.up('Control');
  await wait(200);
  check('ctrl and the wheel zooms', (await zoomOf()) === '0.99', await zoomOf());
  await page.evaluate(() => { document.querySelector('.panel').style.zoom = '0.9'; });

  await page.click('.composer textarea');
  const box = await page.evaluate(() => {
    const r = document.querySelector('.composer textarea').getBoundingClientRect();
    return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) };
  });
  await page.mouse.click(box.x, box.y, { button: 'right' });
  await wait(200);
  check('a text field keeps its native menu so paste still works',
    (await page.evaluate(() => document.querySelectorAll('.ctx-menu').length)) === 0);

  // ------------------------------------------------- the C# contract, replayed
  // host-events.json is written by PanelContractTests in the plug-in's test suite, using the real
  // serialiser. Replaying it here is the only place the two languages are checked against each
  // other, so a protocol change on either side fails before it reaches Rhino.
  const contractPath = resolve(root, 'tests/host-events.json');
  const hostPage = resolve(root, 'dist/panel.host.html');
  if (!existsSync(contractPath) || !existsSync(hostPage)) {
    check('the C# contract sample is available', false, 'run `dotnet test tests/StreamJson.Tests` and `npm run build`');
  } else {
    const events = JSON.parse(readFileSync(contractPath, 'utf8'));
    const host = await browser.newPage();
    host.setDefaultTimeout(6000);
    host.on('pageerror', (e) => consoleProblems.push(`[host pageerror] ${e.message}`));
    host.on('console', (m) => { if (m.type() === 'error') consoleProblems.push(`[host] ${m.text().slice(0, 200)}`); });
    await host.setViewport({ width: 420, height: 900 });

    // Stand in for Eto's injected shim, which is what the panel talks to inside Rhino.
    await host.evaluateOnNewDocument(() => {
      // Both pages are file:// and share a localStorage, so the mock page's zoom would leak in.
      try { window.localStorage.clear(); } catch { /* opaque origin */ }
      window.__sent = [];
      window.eto = { postMessage: (message) => window.__sent.push(JSON.parse(message)) };
    });
    await host.goto(`file://${hostPage}`, { waitUntil: 'domcontentloaded' });
    await wait(400);

    const handshake = await host.evaluate(() => window.__sent.map((m) => m.type));
    check('the host build talks to Eto and announces itself', handshake.includes('ready'), JSON.stringify(handshake));
    check('the host build ships no mock host',
      (await host.evaluate(() => document.querySelectorAll('.devbar').length)) === 0);

    await host.evaluate((batch) => { for (const event of batch) window.rhinoAI.receive(event); }, events);
    await wait(600);

    const rendered = await host.evaluate(() => ({
      agent: document.querySelector('.agent-chip .name')?.textContent,
      model: document.querySelector('.agent-chip .model')?.textContent,
      turns: document.querySelectorAll('.turn').length,
      prompt: document.querySelector('.msg-user')?.textContent,
      bold: document.querySelectorAll('.msg-agent strong').length,
      code: document.querySelectorAll('.md-code').length,
      tools: document.querySelectorAll('.tool').length,
      toolOk: document.querySelectorAll('.tool.ok, .tool:not(.running):not(.failed)').length,
      titles: [...document.querySelectorAll('.tool .title')].map((n) => n.textContent),
      wires: [...document.querySelectorAll('.tool .wire')].map((n) => n.textContent),
      question: document.querySelectorAll('.question label').length,
      footerTokens: [...document.querySelectorAll('.turn-foot')].map((n) => n.textContent).join(' '),
    }));

    check('C#-serialised events render a turn', rendered.turns === 1 && rendered.prompt?.includes('Facade'), JSON.stringify(rendered));
    check('the agent list arrives', rendered.agent === 'Claude Code' && rendered.model === 'Opus 5', JSON.stringify(rendered));
    check('streamed markdown and code render', rendered.bold >= 1 && rendered.code === 1, JSON.stringify(rendered));
    check('a tool call and its folded-in result render as one settled card',
      rendered.tools === 2 && rendered.toolOk === 1, JSON.stringify(rendered));
    check('the mcp__rhino__ prefix never reaches the panel',
      !JSON.stringify(rendered).includes('mcp__'), JSON.stringify(rendered));
    check('a namespaced tool gets its real phrase',
      rendered.titles.includes('listed objects'), JSON.stringify(rendered.titles));
    check('the wire name shows only when it adds something',
      rendered.wires.length === 1 && rendered.wires[0] === 'list_objects', JSON.stringify(rendered.wires));
    check('the question posed by the feed renders', rendered.question === 2, JSON.stringify(rendered));
    check('per-turn tokens land on the turn, not the top bar',
      rendered.footerTokens.includes('14k tok') && !rendered.footerTokens.includes('$'),
      JSON.stringify(rendered.footerTokens));
    check('the top bar carries no token or cost readout',
      (await host.evaluate(() => document.querySelectorAll('.header .usage').length)) === 0);
    check('no cost is shown anywhere',
      (await host.evaluate(() => !document.body.textContent.includes('$0.'))) === true);

    // Switching Rhino's theme sends a second theme event. The panel has to restyle from it, both
    // the host-supplied tokens and the scheme attribute that drives everything the host does not
    // send (semantic and syntax colours). The attribute is set by an effect, so each event needs a
    // turn of the microtask queue before it can be read back.
    const readTheme = () =>
      host.evaluate(() => ({
        scheme: document.documentElement.dataset.scheme,
        // The transcript is transparent and shows the body's ground.
        bg: getComputedStyle(document.body).backgroundColor,
        chrome: getComputedStyle(document.querySelector('.header')).backgroundColor,
      }));

    await host.evaluate(() =>
      window.rhinoAI.receive({
        type: 'theme',
        scheme: 'light',
        tokens: { bg: '#ffffff', control: '#ebebed', text: '#111111' },
      }),
    );
    await wait(120);
    const lightTheme = await readTheme();

    await host.evaluate(() =>
      window.rhinoAI.receive({
        type: 'theme',
        scheme: 'dark',
        tokens: { bg: '#141417', control: '#1c1c21', text: '#eeeeee' },
      }),
    );
    await wait(120);
    const darkTheme = await readTheme();

    check('a second theme event restyles the panel',
      lightTheme.bg === 'rgb(255, 255, 255)' && darkTheme.bg === 'rgb(20, 20, 23)',
      JSON.stringify({ lightTheme, darkTheme }));
    check('the chrome and the content restyle independently',
      darkTheme.chrome === 'rgb(28, 28, 33)' && darkTheme.chrome !== darkTheme.bg,
      JSON.stringify(darkTheme));
    check('the scheme attribute follows, so unsent tokens switch too',
      lightTheme.scheme === 'light' && darkTheme.scheme === 'dark',
      JSON.stringify({ light: lightTheme.scheme, dark: darkTheme.scheme }));

    // Without any host tokens the stylesheet has to carry a complete theme on its own, or a page
    // that has not been sent one (a fresh load, a suppressed send) renders half in each theme.
    const stylesheetThemes = await host.evaluate(() => {
      const names = ['--bg', '--control', '--control-hover', '--field', '--text', '--link',
                     '--selection', '--selection-text', '--rule', '--surface', '--border', '--shadow'];
      const root = document.documentElement;
      const saved = names.map((n) => [n, root.style.getPropertyValue(n)]);
      for (const [n] of saved) root.style.removeProperty(n);

      const read = () => Object.fromEntries(
        names.map((n) => [n, getComputedStyle(root).getPropertyValue(n).trim()]));

      root.dataset.scheme = 'light';
      const light = read();
      root.dataset.scheme = 'dark';
      const dark = read();

      for (const [n, v] of saved) if (v) root.style.setProperty(n, v);
      return { light, dark, missing: names.filter((n) => !light[n] || !dark[n]),
               same: names.filter((n) => light[n] === dark[n]) };
    });
    check('the stylesheet defines every theme token in both schemes',
      stylesheetThemes.missing.length === 0, JSON.stringify(stylesheetThemes.missing));
    check('every theme token actually changes between the schemes',
      stylesheetThemes.same.length === 0, JSON.stringify(stylesheetThemes.same));

    // A host that re-announces what it already sent must not double the transcript. This is the
    // receiver half of the guard; the sender half is ConversationFeed keeping its high-water marks.
    const beforeReplay = await host.evaluate(() => ({
      turns: document.querySelectorAll('.turn').length,
      tools: document.querySelectorAll('.tool').length,
      users: document.querySelectorAll('.msg-user').length,
    }));
    await host.evaluate((batch) => { for (const event of batch) window.rhinoAI.receive(event); }, events.filter((e) => e.type.startsWith('turn.')));
    await wait(400);
    const afterReplay = await host.evaluate(() => ({
      turns: document.querySelectorAll('.turn').length,
      tools: document.querySelectorAll('.tool').length,
      users: document.querySelectorAll('.msg-user').length,
    }));
    check('re-announced turns and tool calls do not duplicate rows',
      JSON.stringify(beforeReplay) === JSON.stringify(afterReplay),
      `${JSON.stringify(beforeReplay)} -> ${JSON.stringify(afterReplay)}`);

    // Right click is the host's job now, so the panel must ask rather than draw.
    await host.evaluate(() => {
      window.__sent.length = 0;
      document.querySelector('.transcript').dispatchEvent(
        new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 120, clientY: 260 }),
      );
    });
    await wait(150);
    const menuRequest = await host.evaluate(() => window.__sent.find((m) => m.type === 'menu.open'));
    check('right click asks the host for a native menu',
      menuRequest !== undefined && menuRequest.x === 120 && menuRequest.y === 260, JSON.stringify(menuRequest));
    check('the request carries what the menu items need',
      menuRequest?.zoomLabel === '100%' && menuRequest.canZoomIn === true && menuRequest.canResetZoom === false,
      JSON.stringify(menuRequest));
    check('no in-page menu is drawn',
      (await host.evaluate(() => document.querySelectorAll('.ctx-menu').length)) === 0);

    // A text field keeps the native field menu, which is the only route to Paste.
    await host.evaluate(() => {
      window.__sent.length = 0;
      document.querySelector('.composer textarea').dispatchEvent(
        new MouseEvent('contextmenu', { bubbles: true, cancelable: true }),
      );
    });
    await wait(120);
    check('a text field is left to the native field menu',
      (await host.evaluate(() => window.__sent.some((m) => m.type === 'menu.open'))) === false);

    await host.evaluate(() => {
      window.__sent.length = 0;
      document.querySelector('.header').dispatchEvent(
        new MouseEvent('contextmenu', { bubbles: true, cancelable: true }),
      );
    });
    await wait(120);
    check('the header gets no zoom menu',
      (await host.evaluate(() => window.__sent.some((m) => m.type === 'menu.open'))) === false);

    // The menu reports an intent; the panel owns the ladder.
    await host.evaluate(() => window.rhinoAI.receive({ type: 'zoom', action: 'in' }));
    await wait(150);
    check('a zoom event from the host moves the panel one rung',
      (await host.evaluate(() => document.querySelector('.panel').style.zoom)) === '0.99',
      await host.evaluate(() => document.querySelector('.panel').style.zoom));

    // Answering has to travel back in the shape the C# deserialiser accepts.
    // Two steps: Answer stays disabled until the selection has propagated.
    await host.evaluate(() => document.querySelectorAll('.question label')[0].click());
    await wait(150);
    await host.evaluate(() => document.querySelector('.question .btn.primary').click());
    await wait(200);
    const answered = await host.evaluate(() => window.__sent.find((m) => m.type === 'question.answer'));
    check('an answer is sent in the shape C# parses',
      answered?.answers?.[0] === 'Yes' && typeof answered.id === 'string', JSON.stringify(answered));

    await host.close();
  }

  check('no console errors or crashes', consoleProblems.length === 0, consoleProblems.join(' | '));
} finally {
  await browser.close();
}

console.log(`\n${failures.length === 0 ? 'verify: all checks passed' : `verify: ${failures.length} failed`}`);
process.exit(failures.length === 0 ? 0 : 1);
