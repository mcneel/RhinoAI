import assert from 'node:assert/strict';
import test from 'node:test';
import { formatBytes, formatCost, formatDuration, formatTokens, prettyJson, relativeTime, summarize } from './format.ts';

test('relative time buckets', () => {
  const now = Date.parse('2026-09-03T12:00:00Z');
  const ago = (ms: number) => relativeTime(new Date(now - ms).toISOString(), now);

  assert.equal(ago(5_000), 'just now');
  assert.equal(ago(44_000), 'just now');
  assert.equal(ago(5 * 60_000), '5m ago');
  assert.equal(ago(3 * 3_600_000), '3h ago');
  assert.equal(ago(2 * 86_400_000), '2d ago');
  assert.equal(relativeTime('not a date', now), '');
});

test('token formatting', () => {
  assert.equal(formatTokens(0), '0');
  assert.equal(formatTokens(999), '999');
  assert.equal(formatTokens(1_500), '1.5k');
  assert.equal(formatTokens(41_200), '41k');
  assert.equal(formatTokens(2_400_000), '2.4M');
});

test('cost keeps null distinct from zero', () => {
  assert.equal(formatCost(null), null, 'tokens-only turns must not claim a cost');
  assert.equal(formatCost(0), '<$0.01');
  assert.equal(formatCost(0.624), '$0.62');
});

test('duration formatting', () => {
  assert.equal(formatDuration(410), '410ms');
  assert.equal(formatDuration(1_260), '1.3s');
  assert.equal(formatDuration(45_000), '45s');
  assert.equal(formatDuration(95_000), '1m 35s');
});

test('byte formatting', () => {
  assert.equal(formatBytes(512), '512 B');
  assert.equal(formatBytes(2_048), '2 KB');
  assert.equal(formatBytes(3_500_000), '3.3 MB');
});

test('prettyJson passes strings through and never throws on a cycle', () => {
  assert.equal(prettyJson('already text'), 'already text');
  assert.equal(prettyJson({ a: 1 }), '{\n  "a": 1\n}');
  assert.equal(prettyJson(undefined), '');
  const cyclic: Record<string, unknown> = {};
  cyclic['self'] = cyclic;
  assert.equal(typeof prettyJson(cyclic), 'string');
});

test('summarize takes the first non-blank line and caps it', () => {
  assert.equal(summarize('\n\n  hello there \nsecond'), 'hello there');
  assert.equal(summarize('x'.repeat(80), 10), `${'x'.repeat(10)}…`);
  assert.equal(summarize('   \n  '), '');
});
