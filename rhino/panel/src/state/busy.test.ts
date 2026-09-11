import assert from 'node:assert/strict';
import test from 'node:test';
import { BUSY_WORDS, WORD_MS, busyWord } from './busy.ts';

test('the word advances once per interval and wraps', () => {
  const words = ['one', 'two', 'three'];
  assert.equal(busyWord(0, 0, words), 'one');
  assert.equal(busyWord(WORD_MS - 1, 0, words), 'one');
  assert.equal(busyWord(WORD_MS, 0, words), 'two');
  assert.equal(busyWord(WORD_MS * 3, 0, words), 'one');
  assert.equal(busyWord(WORD_MS * 2, 2, words), 'two');
});

test('the word survives a nonsense elapsed or an empty list', () => {
  assert.equal(busyWord(-5_000, 0, ['only']), 'only');
  assert.equal(busyWord(1_000, 0, []), '');
});

test('every shipped word fits a narrow docked strip', () => {
  assert.ok(BUSY_WORDS.length > 0);
  for (const word of BUSY_WORDS) assert.ok(word.length <= 12, word);
});
