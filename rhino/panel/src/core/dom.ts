// Declarative DOM with reactive bindings and a keyed list reconciler.
//
// `each` is the piece that matters: it moves existing nodes rather than rebuilding them, so a
// streaming turn only ever touches the row that changed. That is the whole of the hand-rolled
// diffing the Eto panel needed, in one place, generically.

import { effect, signal, type Cleanup } from './signal.js';

export type Reactive<T> = T | (() => T);

export interface Mountable {
  mount(parent: Node, before: Node | null): void;
}

export type Child =
  | Node
  | Mountable
  | string
  | number
  | false
  | null
  | undefined
  | (() => Child)
  | readonly Child[];

type PropValue = unknown;

export interface Props {
  class?: Reactive<string | undefined> | Record<string, Reactive<boolean>>;
  style?: Record<string, Reactive<string | undefined>>;
  text?: Reactive<string | number>;
  ref?: (node: never) => void;
  [key: string]: PropValue;
}

// ---------------------------------------------------------------- ownership

let owner: Cleanup[] | null = null;

export function scope<T>(build: () => T): { value: T; dispose: Cleanup } {
  const cleanups: Cleanup[] = [];
  const previous = owner;
  owner = cleanups;
  try {
    const value = build();
    return {
      value,
      dispose: () => {
        for (const cleanup of [...cleanups].reverse()) cleanup();
        cleanups.length = 0;
      },
    };
  } finally {
    owner = previous;
  }
}

export function onCleanup(cleanup: Cleanup): void {
  owner?.push(cleanup);
}

/** An effect owned by the enclosing scope, so removing the node it drives disposes it. */
export function bind(run: () => void | Cleanup): Cleanup {
  const dispose = effect(run);
  onCleanup(dispose);
  return dispose;
}

function resolve<T>(value: Reactive<T>): T {
  return typeof value === 'function' ? (value as () => T)() : value;
}

// ---------------------------------------------------------------- elements

const PROPERTY_KEYS = new Set(['value', 'checked', 'disabled', 'selected', 'indeterminate', 'open']);

function applyAttribute(node: Element, name: string, value: unknown): void {
  if (PROPERTY_KEYS.has(name)) {
    (node as unknown as Record<string, unknown>)[name] = value;
    return;
  }
  if (value === false || value === null || value === undefined) node.removeAttribute(name);
  else if (value === true) node.setAttribute(name, '');
  else node.setAttribute(name, String(value));
}

function applyProps(node: HTMLElement | SVGElement, props: Props): void {
  for (const [key, value] of Object.entries(props)) {
    if (value === undefined || key === 'ref') continue;

    if (key === 'class') {
      if (typeof value === 'object' && value !== null && typeof value !== 'function') {
        for (const [name, on] of Object.entries(value as Record<string, Reactive<boolean>>))
          bind(() => {
            node.classList.toggle(name, Boolean(resolve(on)));
          });
      } else {
        bind(() => { node.setAttribute('class', resolve(value as Reactive<string | undefined>) ?? ''); });
      }
      continue;
    }

    if (key === 'style') {
      for (const [name, raw] of Object.entries(value as Record<string, Reactive<string | undefined>>))
        bind(() => node.style.setProperty(name, resolve(raw) ?? null));
      continue;
    }

    if (key === 'text') {
      bind(() => { node.textContent = String(resolve(value as Reactive<string | number>)); });
      continue;
    }

    if (key.startsWith('on') && typeof value === 'function') {
      node.addEventListener(key.slice(2).toLowerCase(), value as EventListener);
      continue;
    }

    if (typeof value === 'function') bind(() => applyAttribute(node, key, (value as () => unknown)()));
    else applyAttribute(node, key, value);
  }
}

export function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  props?: Props | null,
  ...children: Child[]
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (props) applyProps(node, props);
  for (const child of children) append(node, child);
  if (props?.ref) (props.ref as (n: HTMLElement) => void)(node);
  return node;
}

export function svg(tag: string, props?: Props | null, ...children: Child[]): SVGElement {
  const node = document.createElementNS('http://www.w3.org/2000/svg', tag) as SVGElement;
  if (props) applyProps(node, props);
  for (const child of children) append(node, child);
  return node;
}

export function frag(...children: Child[]): DocumentFragment {
  const fragment = document.createDocumentFragment();
  for (const child of children) append(fragment, child);
  return fragment;
}

function isMountable(value: unknown): value is Mountable {
  return typeof value === 'object' && value !== null && typeof (value as Mountable).mount === 'function';
}

export function append(parent: Node, child: Child): void {
  if (child === null || child === undefined || child === false) return;

  if (typeof child === 'string' || typeof child === 'number') {
    parent.appendChild(document.createTextNode(String(child)));
    return;
  }
  if (Array.isArray(child)) {
    for (const each of child) append(parent, each);
    return;
  }
  if (isMountable(child)) {
    child.mount(parent, null);
    return;
  }
  if (child instanceof Node) {
    parent.appendChild(child);
    return;
  }
  region(parent, child as () => Child);
}

// ---------------------------------------------------------------- regions

/** A stretch of DOM between two comment markers, rebuilt whenever its inputs change. */
function region(parent: Node, render: () => Child): void {
  const start = document.createComment('');
  const end = document.createComment('');
  parent.appendChild(start);
  parent.appendChild(end);

  let disposeInner: Cleanup | null = null;
  bind(() => {
    let node = start.nextSibling;
    while (node && node !== end) {
      const next = node.nextSibling;
      (node as ChildNode).remove();
      node = next;
    }
    disposeInner?.();

    const built = scope(() => {
      const fragment = document.createDocumentFragment();
      append(fragment, render());
      return fragment;
    });
    disposeInner = built.dispose;
    end.parentNode?.insertBefore(built.value, end);
  });

  onCleanup(() => disposeInner?.());
}

/**
 * A region that rebuilds only when the condition actually flips.
 *
 * The condition is evaluated in its own effect and latched into a signal, so the region subscribes
 * to one boolean rather than to everything the condition happens to touch. Without the latch,
 * `when(() => turns().length === 0, ...)` re-ran (and so rebuilt every row) on each new turn.
 */
export function when(condition: () => boolean, build: () => Child, fallback?: () => Child): Child {
  const state = signal(false);
  bind(() => state.set(condition()));
  return () => (state() ? build() : fallback?.());
}

// ---------------------------------------------------------------- keyed list

export function each<T>(
  items: () => readonly T[],
  keyOf: (item: T) => string,
  build: (item: T) => Child,
): Mountable {
  return {
    mount(host, before) {
      const end = document.createComment('each');
      host.insertBefore(end, before);

      interface Row {
        nodes: Node[];
        dispose: Cleanup;
      }
      let rows = new Map<string, Row>();

      bind(() => {
        // The anchor knows where the list actually lives: `host` may have been a DocumentFragment
        // that was emptied into the document after mount.
        const parent = end.parentNode;
        if (!parent) return;

        const list = items();
        const next = new Map<string, Row>();
        let cursor: Node = end;

        // Backwards, so each row lands immediately before the row that follows it and an unchanged
        // tail is never touched.
        for (let i = list.length - 1; i >= 0; i--) {
          const item = list[i] as T;
          const key = keyOf(item);
          let row = rows.get(key);
          if (row) {
            rows.delete(key);
          } else {
            const built = scope(() => {
              const fragment = document.createDocumentFragment();
              append(fragment, build(item));
              return fragment;
            });
            row = { nodes: [...built.value.childNodes], dispose: built.dispose };
          }

          for (let n = row.nodes.length - 1; n >= 0; n--) {
            const node = row.nodes[n] as Node;
            if (node.parentNode !== parent || node.nextSibling !== cursor)
              parent.insertBefore(node, cursor);
            cursor = node;
          }
          next.set(key, row);
        }

        for (const stale of rows.values()) {
          stale.dispose();
          for (const node of stale.nodes) (node as ChildNode).remove();
        }
        rows = next;
      });

      onCleanup(() => {
        for (const row of rows.values()) row.dispose();
      });
    },
  };
}

export function mount(parent: ParentNode, build: () => Child): Cleanup {
  const built = scope(() => {
    const fragment = document.createDocumentFragment();
    append(fragment, build());
    return fragment;
  });
  parent.appendChild(built.value);
  return built.dispose;
}
