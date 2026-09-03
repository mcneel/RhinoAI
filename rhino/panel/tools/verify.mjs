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
  check('a new conversation shows the empty state', empty.turns === 0 && empty.starters >= 4, JSON.stringify(empty));

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
      question: document.querySelectorAll('.question label').length,
      usage: document.querySelector('.usage')?.textContent?.trim(),
    }));

    check('C#-serialised events render a turn', rendered.turns === 1 && rendered.prompt?.includes('Facade'), JSON.stringify(rendered));
    check('the agent list arrives', rendered.agent === 'Claude Code' && rendered.model === 'Opus 5', JSON.stringify(rendered));
    check('streamed markdown and code render', rendered.bold >= 1 && rendered.code === 1, JSON.stringify(rendered));
    check('a tool call and its folded-in result render as one settled card',
      rendered.tools === 1 && rendered.toolOk === 1, JSON.stringify(rendered));
    check('the question posed by the feed renders', rendered.question === 2, JSON.stringify(rendered));
    check('per-turn usage reaches the header', (rendered.usage ?? '').includes('14k'), JSON.stringify(rendered));

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
