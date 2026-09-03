// Markdown -> DOM. A deliberate subset: what an agent actually writes into a chat reply.
//
// Never builds an HTML string, so model output cannot inject markup. Unterminated fences render as
// code anyway, which is what a reply still streaming looks like.

import { highlight, normalizeLanguage } from './highlight.js';

export interface MarkdownOptions {
  /** Rendered as a button on every code block when supplied. */
  copy?: (text: string) => void;
  /** A webview cannot navigate itself, so links are handed back to the host. */
  openLink?: (url: string) => void;
}

const FENCE = /^ {0,3}(```+|~~~+)\s*([\w#+.-]*)\s*$/;
const HEADING = /^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$/;
const RULE = /^ {0,3}(?:-{3,}|\*{3,}|_{3,})\s*$/;
const QUOTE = /^ {0,3}> ?(.*)$/;
const ITEM = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/;
const TABLE_RULE = /^\s*\|?[\s:-]*-[\s:|-]*\|?\s*$/;

const INLINE = new RegExp(
  [
    /`([^`\n]+)`/,
    /\*\*([\s\S]+?)\*\*/,
    /__([\s\S]+?)__/,
    /\*([^*\n]+)\*/,
    /_([^_\n]+)_(?![A-Za-z0-9])/,
    /~~([\s\S]+?)~~/,
    /\[([^\]\n]*)\]\(([^)\s]+)\)/,
    /(https?:\/\/[^\s<>()[\]]+)/,
  ]
    .map((r) => `(?:${r.source})`)
    .join('|'),
  'g',
);

function element(tag: string, className?: string): HTMLElement {
  const node = document.createElement(tag);
  if (className) node.className = className;
  return node;
}

function inline(source: string, options: MarkdownOptions): DocumentFragment {
  const fragment = document.createDocumentFragment();
  // A fresh matcher per call: inline() recurses into emphasis and link bodies, and a shared `g`
  // regex would have its lastIndex rewound by the inner call and never terminate.
  const scanner = new RegExp(INLINE.source, 'g');
  let cursor = 0;
  let match: RegExpExecArray | null;

  const push = (text: string) => {
    // A single newline inside a paragraph reads as a break in a chat reply, not a space.
    const parts = text.split('\n');
    parts.forEach((part, index) => {
      if (index > 0) fragment.appendChild(element('br'));
      if (part) fragment.appendChild(document.createTextNode(part));
    });
  };

  while ((match = scanner.exec(source)) !== null) {
    if (match.index > cursor) push(source.slice(cursor, match.index));
    cursor = match.index + match[0].length;

    const [, code, strongStar, strongUnder, emStar, emUnder, strike, linkText, linkHref, bareUrl] = match;

    // Underscore emphasis must not fire mid-identifier (`layer_name_2`). A lookbehind would say this
    // in the pattern, but lookbehind only landed in Safari 16.4 and a module-level RegExp that
    // throws takes the whole panel down, so the boundary check lives here instead.
    if ((strongUnder !== undefined || emUnder !== undefined) && /[\w]/.test(source[match.index - 1] ?? '')) {
      push(match[0]);
      continue;
    }

    if (code !== undefined) {
      const node = element('code');
      node.textContent = code;
      fragment.appendChild(node);
    } else if (strongStar !== undefined || strongUnder !== undefined) {
      const node = element('strong');
      node.appendChild(inline((strongStar ?? strongUnder) as string, options));
      fragment.appendChild(node);
    } else if (emStar !== undefined || emUnder !== undefined) {
      const node = element('em');
      node.appendChild(inline((emStar ?? emUnder) as string, options));
      fragment.appendChild(node);
    } else if (strike !== undefined) {
      const node = element('s');
      node.appendChild(inline(strike, options));
      fragment.appendChild(node);
    } else if (linkHref !== undefined) {
      fragment.appendChild(anchor(linkHref, linkText || linkHref, options));
    } else if (bareUrl !== undefined) {
      fragment.appendChild(anchor(bareUrl, bareUrl, options));
    }
  }

  if (cursor < source.length) push(source.slice(cursor));
  return fragment;
}

function anchor(href: string, label: string, options: MarkdownOptions): HTMLElement {
  const node = element('a') as HTMLAnchorElement;
  node.href = href;
  node.textContent = label;
  node.addEventListener('click', (event) => {
    event.preventDefault();
    options.openLink?.(href);
  });
  return node;
}

function codeBlock(code: string, language: string, options: MarkdownOptions): HTMLElement {
  const figure = element('figure', 'md-code');
  const caption = element('figcaption');
  const label = element('span', 'md-code-lang');
  label.textContent = normalizeLanguage(language) || 'text';
  caption.appendChild(label);

  if (options.copy) {
    const button = element('button', 'md-code-copy') as HTMLButtonElement;
    button.type = 'button';
    button.textContent = 'Copy';
    button.addEventListener('click', () => {
      options.copy?.(code);
      button.textContent = 'Copied';
      setTimeout(() => { button.textContent = 'Copy'; }, 1200);
    });
    caption.appendChild(button);
  }

  const pre = element('pre');
  const codeNode = element('code');
  codeNode.appendChild(highlight(code, language));
  pre.appendChild(codeNode);

  figure.appendChild(caption);
  figure.appendChild(pre);
  return figure;
}

/** Tables scroll inside their own box rather than widening the transcript. */
function el_wrap(table: HTMLElement): HTMLElement {
  const wrap = element('div', 'md-table');
  wrap.appendChild(table);
  return wrap;
}

function indentWidth(line: string): number {
  const match = /^\s*/.exec(line);
  return match ? match[0].replace(/\t/g, '    ').length : 0;
}

function blocks(lines: readonly string[], options: MarkdownOptions): DocumentFragment {
  const fragment = document.createDocumentFragment();
  let index = 0;

  while (index < lines.length) {
    const line = lines[index] as string;

    if (line.trim() === '') {
      index++;
      continue;
    }

    const fence = FENCE.exec(line);
    if (fence) {
      const marker = fence[1] as string;
      const body: string[] = [];
      index++;
      while (index < lines.length && !(lines[index] as string).trimStart().startsWith(marker)) {
        body.push(lines[index] as string);
        index++;
      }
      index++; // closing fence, or past the end while still streaming
      fragment.appendChild(codeBlock(body.join('\n'), fence[2] ?? '', options));
      continue;
    }

    const heading = HEADING.exec(line);
    if (heading) {
      const node = element(`h${(heading[1] as string).length}`);
      node.appendChild(inline(heading[2] as string, options));
      fragment.appendChild(node);
      index++;
      continue;
    }

    if (RULE.test(line)) {
      fragment.appendChild(element('hr'));
      index++;
      continue;
    }

    if (QUOTE.test(line)) {
      const body: string[] = [];
      while (index < lines.length) {
        const quoted = QUOTE.exec(lines[index] as string);
        if (!quoted) break;
        body.push(quoted[1] as string);
        index++;
      }
      const node = element('blockquote');
      node.appendChild(blocks(body, options));
      fragment.appendChild(node);
      continue;
    }

    const nextLine = lines[index + 1];
    if (line.includes('|') && nextLine !== undefined && nextLine.includes('-') && TABLE_RULE.test(nextLine)) {
      const cells = (row: string) =>
        row.replace(/^\s*\|/, '').replace(/\|\s*$/, '').split('|').map((cell) => cell.trim());
      const table = element('table');
      const head = element('thead');
      const headRow = element('tr');
      for (const cell of cells(line)) {
        const th = element('th');
        th.appendChild(inline(cell, options));
        headRow.appendChild(th);
      }
      head.appendChild(headRow);
      table.appendChild(head);

      const body = element('tbody');
      index += 2;
      while (index < lines.length && (lines[index] as string).includes('|')) {
        const tr = element('tr');
        for (const cell of cells(lines[index] as string)) {
          const td = element('td');
          td.appendChild(inline(cell, options));
          tr.appendChild(td);
        }
        body.appendChild(tr);
        index++;
      }
      table.appendChild(body);
      fragment.appendChild(el_wrap(table));
      continue;
    }

    const item = ITEM.exec(line);
    if (item) {
      const ordered = /\d/.test(item[2] as string);
      const baseIndent = indentWidth(line);
      const list = element(ordered ? 'ol' : 'ul');

      while (index < lines.length) {
        const current = lines[index] as string;
        const nextItem = ITEM.exec(current);
        if (!nextItem || indentWidth(current) < baseIndent) break;
        if (indentWidth(current) > baseIndent) break;

        const body: string[] = [nextItem[3] as string];
        index++;
        // Continuation and nested lines belong to this item.
        while (index < lines.length) {
          const follow = lines[index] as string;
          if (follow.trim() === '') break;
          if (ITEM.test(follow) && indentWidth(follow) <= baseIndent) break;
          body.push(follow.slice(Math.min(indentWidth(follow), baseIndent + 2)));
          index++;
        }

        const li = element('li');
        const nested = body.findIndex((l) => ITEM.test(l));
        if (nested > 0) {
          li.appendChild(inline(body.slice(0, nested).join('\n').trim(), options));
          li.appendChild(blocks(body.slice(nested), options));
        } else {
          li.appendChild(inline(body.join('\n').trim(), options));
        }
        list.appendChild(li);
      }
      fragment.appendChild(list);
      continue;
    }

    const paragraph: string[] = [];
    while (index < lines.length) {
      const current = lines[index] as string;
      if (
        current.trim() === '' ||
        FENCE.test(current) ||
        HEADING.test(current) ||
        RULE.test(current) ||
        QUOTE.test(current) ||
        ITEM.test(current) ||
        (current.includes('|') && TABLE_RULE.test(lines[index + 1] ?? ''))
      )
        break;
      paragraph.push(current);
      index++;
    }
    const node = element('p');
    node.appendChild(inline(paragraph.join('\n'), options));
    fragment.appendChild(node);
  }

  return fragment;
}

export function renderMarkdown(source: string, options: MarkdownOptions = {}): DocumentFragment {
  return blocks(source.replace(/\r\n?/g, '\n').split('\n'), options);
}
