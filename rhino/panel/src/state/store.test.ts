import assert from 'node:assert/strict';
import test from 'node:test';
import type { AgentAvailability, AgentInfo } from '../protocol/events.ts';
import { Store } from './store.ts';

test('an identical notice bumps the toast already showing instead of stacking', () => {
  const store = new Store();

  store.apply({ type: 'notice', level: 'warn', text: 'Nothing selected.' });
  store.apply({ type: 'notice', level: 'warn', text: 'Nothing selected.' });
  store.apply({ type: 'notice', level: 'warn', text: 'Nothing selected.' });

  assert.equal(store.notices().length, 1);
  assert.equal(store.notices()[0]?.repeats(), 2);
});

test('a different text or level is its own notice', () => {
  const store = new Store();

  store.apply({ type: 'notice', level: 'info', text: 'Saved.' });
  store.apply({ type: 'notice', level: 'info', text: 'Saved to disk.' });
  store.apply({ type: 'notice', level: 'error', text: 'Saved.' });

  assert.deepEqual(
    store.notices().map((notice) => `${notice.level}:${notice.text}`),
    ['info:Saved.', 'info:Saved to disk.', 'error:Saved.'],
  );
});

test('the same text shows again once the earlier one is gone', () => {
  const store = new Store();

  store.apply({ type: 'notice', level: 'info', text: 'Reverted that turn.' });
  const first = store.notices()[0];
  assert.ok(first);
  store.dismissNotice(first.id);
  store.apply({ type: 'notice', level: 'info', text: 'Reverted that turn.' });

  assert.equal(store.notices().length, 1);
  assert.notEqual(store.notices()[0]?.id, first.id);
});

test('ready agents come first, disabled ones last, host order kept within each group', () => {
  const store = new Store();
  const agent = (name: string, availability: AgentAvailability): AgentInfo => ({
    name,
    label: name,
    model: '',
    modelLabel: '',
    availability,
    builtin: true,
  });

  store.apply({
    type: 'agents',
    agents: [
      agent('a', 'disabled'),
      agent('b', 'missing'),
      agent('c', 'ready'),
      agent('d', 'signin'),
      agent('e', 'disabled'),
      agent('f', 'ready'),
    ],
    active: 'c',
  });

  assert.deepEqual(
    store.agentsByAvailability().map((a) => a.name),
    ['c', 'f', 'd', 'b', 'a', 'e'],
  );
});

test('the mcp list belongs to one agent and is forgotten when the agent changes', () => {
  const store = new Store();
  store.apply({ type: 'agents', agents: [], active: 'claude' });
  store.apply({ type: 'mcps', mcps: [{ name: 'Rhino', kind: 'stdio', tools: 3, enabled: true, canToggle: true }] });

  store.apply({ type: 'agents', agents: [], active: 'claude' });
  assert.equal(store.mcps()?.length, 1);

  store.apply({ type: 'agents', agents: [], active: 'codex' });
  assert.equal(store.mcps(), null);
});

test('a switch flips at once, but never on an mcp the host has not created yet', () => {
  const store = new Store();
  store.apply({
    type: 'mcps',
    mcps: [
      { name: 'Rhino', kind: 'stdio', tools: 3, enabled: true, canToggle: true },
      { name: 'rhino', kind: 'http', tools: 0, enabled: true, canToggle: false },
    ],
  });

  store.setMcpEnabled('Rhino', false);
  store.setMcpEnabled('rhino', false);

  assert.deepEqual(store.mcps()?.map((mcp) => mcp.enabled), [false, true]);
});

test('a lost session ends the live conversation without dropping its transcript', () => {
  const store = new Store();
  store.apply({
    type: 'conversation',
    snapshot: { sessionId: 's', agent: 'claude', docTitle: 'd', startedAt: '', turns: [], mode: 'live', resumable: true },
  });
  store.apply({
    type: 'turn.begin',
    turn: { id: 't', prompt: 'p', attachments: [], context: [], startedAt: '', status: 'running', usage: null, blocks: [], plan: [], undoable: false },
  });

  store.apply({ type: 'conversation.mode', mode: 'ended', resumable: false });

  assert.equal(store.readOnly(), true);
  assert.equal(store.session()?.mode, 'ended');
  assert.equal(store.session()?.resumable, false);
  assert.equal(store.turns().length, 1);
});
