// A ~90 line reactive core: enough for a panel, small enough to read in one sitting.
//
// Effects are scheduled on a microtask, so a burst of writes in one event handler (or one batch of
// host events) collapses into a single DOM pass without an explicit batch() call.

export type Cleanup = () => void;

interface Source {
  readonly subs: Set<Reaction>;
}

interface Reaction {
  readonly deps: Set<Source>;
  notify(): void;
}

let active: Reaction | null = null;

function track(source: Source): void {
  if (!active) return;
  source.subs.add(active);
  active.deps.add(source);
}

function notify(source: Source): void {
  if (source.subs.size === 0) return;
  for (const reaction of [...source.subs]) reaction.notify();
}

function untrackDeps(reaction: Reaction): void {
  for (const dep of reaction.deps) dep.subs.delete(reaction);
  reaction.deps.clear();
}

/** Read with `s()`, write with `s.set(v)` or `s.set(prev => next)`. */
export interface ReadSignal<T> {
  (): T;
  /** Read without subscribing the enclosing effect. */
  peek(): T;
}

export interface Signal<T> extends ReadSignal<T> {
  set(next: T | ((prev: T) => T)): void;
}

export function signal<T>(initial: T, equals: (a: T, b: T) => boolean = Object.is): Signal<T> {
  const source: Source = { subs: new Set() };
  let value = initial;

  const read = (() => {
    track(source);
    return value;
  }) as Signal<T>;

  read.peek = () => value;
  // A function-typed T would be swallowed by the updater check; the panel never stores one.
  read.set = (next) => {
    const resolved = typeof next === 'function' ? (next as (prev: T) => T)(value) : next;
    if (equals(value, resolved)) return;
    value = resolved;
    notify(source);
  };

  return read;
}

export function computed<T>(compute: () => T): ReadSignal<T> {
  const source: Source = { subs: new Set() };
  let value: T;
  let stale = true;

  const reaction: Reaction = {
    deps: new Set(),
    notify() {
      if (stale) return;
      stale = true;
      notify(source);
    },
  };

  const evaluate = (): T => {
    if (stale) {
      stale = false;
      untrackDeps(reaction);
      const previous = active;
      active = reaction;
      try {
        value = compute();
      } finally {
        active = previous;
      }
    }
    return value;
  };

  const read = (() => {
    const current = evaluate();
    track(source);
    return current;
  }) as ReadSignal<T>;

  read.peek = () => evaluate();
  return read;
}

export function effect(run: () => void | Cleanup): Cleanup {
  let cleanup: void | Cleanup;
  let disposed = false;
  let queued = false;

  const reaction: Reaction = {
    deps: new Set(),
    notify() {
      if (disposed || queued) return;
      queued = true;
      queueMicrotask(execute);
    },
  };

  const execute = (): void => {
    queued = false;
    if (disposed) return;
    if (typeof cleanup === 'function') cleanup();
    untrackDeps(reaction);
    const previous = active;
    active = reaction;
    try {
      cleanup = run();
    } finally {
      active = previous;
    }
  };

  execute();

  return () => {
    disposed = true;
    if (typeof cleanup === 'function') cleanup();
    untrackDeps(reaction);
  };
}

export function untrack<T>(read: () => T): T {
  const previous = active;
  active = null;
  try {
    return read();
  } finally {
    active = previous;
  }
}
