import assert from 'node:assert/strict';
import test from 'node:test';
import { Zoom } from './zoom.ts';
import { DEFAULT } from './zoomSteps.ts';

function tracked(): { zoom: Zoom; sent: number[] } {
  const sent: number[] = [];
  return { zoom: new Zoom((level) => sent.push(level)), sent };
}

test('a level the user picks is handed to the host to store', () => {
  const { zoom, sent } = tracked();
  zoom.in();
  zoom.in();
  assert.deepEqual(sent, [1.1, 1.25]);
  assert.equal(zoom.value(), 1.25);
});

test('a stored level is applied without being echoed straight back', () => {
  const { zoom, sent } = tracked();
  zoom.restore(1.5);
  assert.equal(zoom.value(), 1.5);
  assert.deepEqual(sent, []);
  zoom.out();
  assert.deepEqual(sent, [1.25], 'stepping continues from the restored level');
});

test('a stored level off the ladder snaps, and a nonsense one falls back', () => {
  const { zoom } = tracked();
  zoom.restore(1.4);
  assert.equal(zoom.value(), 1.5);
  zoom.restore(0);
  assert.equal(zoom.value(), DEFAULT);
  zoom.restore(Number.NaN);
  assert.equal(zoom.value(), DEFAULT);
});

test('a step at the end of the ladder does not write the same level again', () => {
  const { zoom, sent } = tracked();
  zoom.restore(2);
  zoom.in();
  assert.deepEqual(sent, []);
  zoom.reset();
  assert.deepEqual(sent, [DEFAULT]);
});
