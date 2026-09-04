import assert from 'node:assert/strict';
import test from 'node:test';
import { computed, effect, signal, untrack } from './signal.ts';

const settle = () => new Promise<void>((resolve) => queueMicrotask(() => queueMicrotask(resolve)));

test('signal reads and writes', () => {
  const count = signal(1);
  assert.equal(count(), 1);
  count.set(2);
  assert.equal(count(), 2);
  count.set((previous) => previous + 3);
  assert.equal(count(), 5);
});

test('an effect runs once immediately and again per change', async () => {
  const count = signal(0);
  const seen: number[] = [];
  effect(() => { seen.push(count()); });
  assert.deepEqual(seen, [0]);

  count.set(1);
  await settle();
  assert.deepEqual(seen, [0, 1]);
});

test('a burst of writes collapses into one effect run', async () => {
  const count = signal(0);
  let runs = 0;
  effect(() => { count(); runs++; });
  assert.equal(runs, 1);

  count.set(1);
  count.set(2);
  count.set(3);
  await settle();
  assert.equal(runs, 2);
  assert.equal(count(), 3);
});

test('an equal write notifies nobody', async () => {
  const name = signal('a');
  let runs = 0;
  effect(() => { name(); runs++; });
  name.set('a');
  await settle();
  assert.equal(runs, 1);
});

test('computed is lazy, memoized, and invalidates through a chain', () => {
  const count = signal(2);
  let evaluations = 0;
  const doubled = computed(() => { evaluations++; return count() * 2; });
  const quadrupled = computed(() => doubled() * 2);

  assert.equal(evaluations, 0);
  assert.equal(quadrupled(), 8);
  assert.equal(evaluations, 1);
  assert.equal(quadrupled(), 8);
  assert.equal(evaluations, 1);

  count.set(3);
  assert.equal(quadrupled(), 12);
  assert.equal(evaluations, 2);
});

test('a disposed effect stops running and drops its subscriptions', async () => {
  const count = signal(0);
  let runs = 0;
  const dispose = effect(() => { count(); runs++; });
  dispose();
  count.set(1);
  await settle();
  assert.equal(runs, 1);
});

test('an effect cleanup runs before its next pass and on dispose', async () => {
  const count = signal(0);
  const log: string[] = [];
  const dispose = effect(() => {
    const value = count();
    log.push(`run:${value}`);
    return () => log.push(`clean:${value}`);
  });

  count.set(1);
  await settle();
  dispose();
  assert.deepEqual(log, ['run:0', 'clean:0', 'run:1', 'clean:1']);
});

test('dependencies are re-collected each pass, so a dropped branch stops firing', async () => {
  const useLeft = signal(true);
  const left = signal('L');
  const right = signal('R');
  const seen: string[] = [];

  effect(() => { seen.push(useLeft() ? left() : right()); });
  assert.deepEqual(seen, ['L']);

  useLeft.set(false);
  await settle();
  assert.deepEqual(seen, ['L', 'R']);

  left.set('L2');
  await settle();
  assert.deepEqual(seen, ['L', 'R'], 'the untaken branch must no longer be a dependency');

  right.set('R2');
  await settle();
  assert.deepEqual(seen, ['L', 'R', 'R2']);
});

test('untrack and peek read without subscribing', async () => {
  const tracked = signal(0);
  const hidden = signal(0);
  let runs = 0;

  effect(() => {
    tracked();
    untrack(() => hidden());
    hidden.peek();
    runs++;
  });

  hidden.set(1);
  await settle();
  assert.equal(runs, 1);

  tracked.set(1);
  await settle();
  assert.equal(runs, 2);
});
