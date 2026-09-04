// A small tokenizer for the languages an agent actually emits into a Rhino conversation.
// One master alternation per grammar, one pass, DOM out. Unknown languages fall through to plain
// text rather than guessing.

type TokenClass = 'com' | 'str' | 'num' | 'kw' | 'type' | 'fn' | 'attr' | 'punct';

interface Grammar {
  pattern: RegExp;
  /** Group index (1-based) -> class, for the non-identifier groups. */
  groups: readonly (TokenClass | 'ident')[];
  keywords?: ReadonlySet<string>;
  types?: ReadonlySet<string>;
}

const set = (words: string) => new Set(words.split(/\s+/).filter(Boolean));

const PYTHON_KEYWORDS = set(`
  and as assert async await break class continue def del elif else except finally for from global
  if import in is lambda nonlocal not or pass raise return try while with yield None True False
`);
const PYTHON_TYPES = set(`
  self cls print len range int float str bool list dict set tuple enumerate zip map filter sum abs
  min max round open isinstance type super Exception ValueError TypeError
`);

const CSHARP_KEYWORDS = set(`
  abstract as async await base bool break byte case catch char checked class const continue decimal
  default delegate do double else enum event explicit extern false finally fixed float for foreach
  get goto if implicit in int interface internal is lock long namespace new null object operator out
  override params private protected public readonly record ref return sbyte sealed set short sizeof
  stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe
  ushort using var virtual void volatile where while with yield
`);

const JS_KEYWORDS = set(`
  as async await break case catch class const continue debugger default delete do else enum export
  extends false finally for from function get if implements import in instanceof interface let new
  null of private protected public readonly return satisfies set static super switch this throw
  true try type typeof undefined var void while with yield
`);

const IDENT = String.raw`[A-Za-z_$][\w$]*`;

const GRAMMARS: Record<string, Grammar> = {
  python: {
    pattern: new RegExp(
      [
        String.raw`(#[^\n]*)`,
        String.raw`((?:[rbfuRBFU]{0,2})(?:"""[\s\S]*?"""|'''[\s\S]*?'''|"(?:\\.|[^"\\\n])*"|'(?:\\.|[^'\\\n])*'))`,
        String.raw`(\b\d[\w.]*)`,
        String.raw`(@${IDENT})`,
        String.raw`(${IDENT})`,
      ].join('|'),
      'g',
    ),
    groups: ['com', 'str', 'num', 'attr', 'ident'],
    keywords: PYTHON_KEYWORDS,
    types: PYTHON_TYPES,
  },
  csharp: {
    pattern: new RegExp(
      [
        String.raw`(\/\/[^\n]*|\/\*[\s\S]*?\*\/)`,
        String.raw`(@?\$?"(?:""|\\.|[^"\\])*"|'(?:\\.|[^'\\])*')`,
        String.raw`(\b\d[\w.]*)`,
        String.raw`(\[\s*[A-Z]\w*(?:\([^)]*\))?\s*\])`,
        String.raw`(${IDENT})`,
      ].join('|'),
      'g',
    ),
    groups: ['com', 'str', 'num', 'attr', 'ident'],
    keywords: CSHARP_KEYWORDS,
  },
  javascript: {
    pattern: new RegExp(
      [
        String.raw`(\/\/[^\n]*|\/\*[\s\S]*?\*\/)`,
        String.raw`(\`(?:\\.|[^\\\`])*\`|"(?:\\.|[^"\\\n])*"|'(?:\\.|[^'\\\n])*')`,
        String.raw`(\b\d[\w.]*)`,
        String.raw`(${IDENT})`,
      ].join('|'),
      'g',
    ),
    groups: ['com', 'str', 'num', 'ident'],
    keywords: JS_KEYWORDS,
  },
  json: {
    pattern: new RegExp(
      [
        String.raw`("(?:\\.|[^"\\])*"(?=\s*:))`,
        String.raw`("(?:\\.|[^"\\])*")`,
        String.raw`(-?\b\d[\d.eE+-]*)`,
        String.raw`(\btrue\b|\bfalse\b|\bnull\b)`,
      ].join('|'),
      'g',
    ),
    groups: ['attr', 'str', 'num', 'kw'],
  },
};

const ALIASES: Record<string, string> = {
  py: 'python',
  cs: 'csharp',
  'c#': 'csharp',
  js: 'javascript',
  ts: 'javascript',
  typescript: 'javascript',
  jsonc: 'json',
};

export function normalizeLanguage(raw: string): string {
  const lower = raw.trim().toLowerCase();
  return ALIASES[lower] ?? lower;
}

function span(cls: TokenClass, text: string): HTMLElement {
  const node = document.createElement('span');
  node.className = `tok-${cls}`;
  node.textContent = text;
  return node;
}

/** Highlighted code as a fragment; plain text when the language is unknown. */
export function highlight(code: string, language: string): DocumentFragment {
  const fragment = document.createDocumentFragment();
  const grammar = GRAMMARS[normalizeLanguage(language)];
  if (!grammar) {
    fragment.appendChild(document.createTextNode(code));
    return fragment;
  }

  // A fresh matcher per call: a module-level `g` regex carries lastIndex between callers.
  const scanner = new RegExp(grammar.pattern.source, 'g');
  let cursor = 0;
  let match: RegExpExecArray | null;

  while ((match = scanner.exec(code)) !== null) {
    if (match[0] === '') {
      scanner.lastIndex += 1;
      continue;
    }
    if (match.index > cursor)
      fragment.appendChild(document.createTextNode(code.slice(cursor, match.index)));
    cursor = match.index + match[0].length;

    let placed = false;
    for (let group = 0; group < grammar.groups.length; group++) {
      const value = match[group + 1];
      if (value === undefined || value === '') continue;
      const kind = grammar.groups[group] as TokenClass | 'ident';
      if (kind === 'ident') {
        if (grammar.keywords?.has(value)) fragment.appendChild(span('kw', value));
        else if (grammar.types?.has(value)) fragment.appendChild(span('type', value));
        else if (code[cursor] === '(') fragment.appendChild(span('fn', value));
        else if (/^[A-Z]/.test(value)) fragment.appendChild(span('type', value));
        else fragment.appendChild(document.createTextNode(value));
      } else {
        fragment.appendChild(span(kind, value));
      }
      placed = true;
      break;
    }
    if (!placed) fragment.appendChild(document.createTextNode(match[0]));
  }

  if (cursor < code.length) fragment.appendChild(document.createTextNode(code.slice(cursor)));
  return fragment;
}
