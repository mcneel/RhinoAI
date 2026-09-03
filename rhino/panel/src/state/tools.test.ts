import assert from 'node:assert/strict';
import test from 'node:test';
import { familyOf, iconFor, statusLabel } from './tools.ts';

test('tool families are resolved by exact name then prefix', () => {
  assert.equal(familyOf('open_doc'), 'document');
  assert.equal(familyOf('get_selection'), 'geometry');
  assert.equal(familyOf('run_python'), 'script');
  assert.equal(familyOf('run_csharp'), 'script');
  assert.equal(familyOf('g1_place_component'), 'grasshopper');
  assert.equal(familyOf('g2_solve_canvas'), 'grasshopper');
  assert.equal(familyOf('get_viewport_image'), 'view');
  assert.equal(familyOf('zoom_to_layer'), 'view');
  assert.equal(familyOf('ask_user'), 'question');
});

test('an unknown tool falls back rather than guessing', () => {
  assert.equal(familyOf('some_future_tool'), 'other');
  assert.equal(iconFor(familyOf('some_future_tool')), 'tool');
});

test('every family has an icon', () => {
  for (const name of ['script', 'document', 'geometry', 'view', 'grasshopper', 'question', 'other'] as const)
    assert.ok(iconFor(name).length > 0);
});

test('status labels', () => {
  assert.equal(statusLabel('running'), 'running');
  assert.equal(statusLabel('ok'), 'done');
  assert.equal(statusLabel('failed'), 'failed');
  assert.equal(statusLabel('denied'), 'denied');
});
