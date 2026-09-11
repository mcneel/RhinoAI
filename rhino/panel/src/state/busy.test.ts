import assert from 'node:assert/strict';
import test from 'node:test';
import { BUSY_KEYS, WORD_MS, busyWord } from './busy.ts';
import { EN, type StringKey } from '../i18n/strings.ts';

const THREE: StringKey[] = ['busy.mulling', 'busy.grazing', 'busy.ruminating'];

test('the word advances once per interval and wraps', () => {
  assert.equal(busyWord(0, 0, THREE), 'busy.mulling');
  assert.equal(busyWord(WORD_MS - 1, 0, THREE), 'busy.mulling');
  assert.equal(busyWord(WORD_MS, 0, THREE), 'busy.grazing');
  assert.equal(busyWord(WORD_MS * 3, 0, THREE), 'busy.mulling');
  assert.equal(busyWord(WORD_MS * 2, 2, THREE), 'busy.grazing');
});

test('the word survives a nonsense elapsed or an empty list', () => {
  assert.equal(busyWord(-5_000, 0, ['busy.honing']), 'busy.honing');
  assert.equal(busyWord(1_000, 0, []), null);
});

test('every shipped word fits a narrow docked strip', () => {
  assert.ok(BUSY_KEYS.length > 0);
  for (const key of BUSY_KEYS) assert.ok(EN[key].length <= 12, key);
});
