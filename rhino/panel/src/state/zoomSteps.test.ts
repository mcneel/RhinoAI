import assert from 'node:assert/strict';
import test from 'node:test';
import { DEFAULT, STEPS, asPercent, canZoomIn, canZoomOut, stepIn, stepOut, toCssZoom } from './zoomSteps.ts';

test('stepping walks the ladder one rung at a time', () => {
  assert.equal(stepIn(1), 1.1);
  assert.equal(stepIn(1.1), 1.25);
  assert.equal(stepOut(1), 0.9);
  assert.equal(stepOut(0.9), 0.8);
});

test('the ladder has ends and stepping stops at them', () => {
  assert.equal(stepIn(2), 2);
  assert.equal(stepOut(0.67), 0.67);
  assert.equal(canZoomIn(2), false);
  assert.equal(canZoomOut(0.67), false);
  assert.equal(canZoomIn(1), true);
  assert.equal(canZoomOut(1), true);
});

test('an off-ladder value snaps to the nearest rung before stepping', () => {
  assert.equal(stepIn(1.02), 1.1, '1.02 is nearest 1, so in goes to 1.1');
  assert.equal(stepOut(1.4), 1.25, '1.4 is nearest 1.5, so out goes to 1.25');
});

test('a wildly out of range value is still handled', () => {
  assert.equal(stepIn(99), 2);
  assert.equal(stepOut(0.001), 0.67);
});

test('percentages read the way a user expects', () => {
  assert.equal(asPercent(1), '100%');
  assert.equal(asPercent(0.67), '67%');
  assert.equal(asPercent(1.75), '175%');
});

test('the default is 100% to the user and sits on the ladder', () => {
  assert.ok(STEPS.includes(DEFAULT), 'a default off the ladder would make the first step erratic');
  assert.equal(DEFAULT, 1);
  assert.equal(asPercent(DEFAULT), '100%');
});

test('the stylesheet baseline is folded into the applied zoom, not into the default', () => {
  // The panel is authored a notch large, so the user's 100% is CSS zoom 0.9.
  assert.equal(toCssZoom(1), 0.9);
  assert.equal(toCssZoom(stepIn(1)), 0.99, 'rounded: 1.1 x 0.9 is 0.9900000000000001');
  assert.equal(toCssZoom(stepOut(1)), 0.81);
  assert.equal(toCssZoom(2), 1.8);
});
