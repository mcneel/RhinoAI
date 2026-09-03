import './styles/panel.css';

import { effect } from './core/signal.js';
import { mount } from './core/dom.js';
import { inertBridge, resolveNativeBridge, type Bridge } from './protocol/bridge.js';
import { MockHost } from './protocol/mockHost.js';
import { Store } from './state/store.js';
import { UiState } from './state/ui.js';
import { app } from './ui/app.js';
import type { PanelContext } from './ui/context.js';
import { devFrame } from './dev/frame.js';
import { engineIsSupported, renderUnsupported } from './ui/unsupported.js';

const native = resolveNativeBridge();
// __MOCK__ is false in a host build, so the mock and the review chrome tree-shake away entirely.
const bridge: Bridge = native ?? (__MOCK__ ? new MockHost() : inertBridge());
const store = new Store();
const ui = new UiState();

const ctx: PanelContext = {
  store,
  ui,
  send: (command) => bridge.send(command),
  copy: (text) => {
    // The host owns the clipboard when the webview cannot reach it (older WKWebView, no https).
    void navigator.clipboard?.writeText(text).catch(() => bridge.send({ type: 'clipboard.write', text }));
  },
  openLink: (url) => bridge.send({ type: 'url.open', url }),
  submit: (override) => {
    if (store.readOnly.peek() || !store.hasReadyAgent.peek()) return;
    const text = (override ?? ui.draft.peek()).trim();
    const attachments = [...ui.attachments.peek()];
    const context = [...ui.pickedContext.peek()];
    if (text.length === 0 && attachments.length === 0) return;

    ui.pinned.set(true);
    ui.hasNew.set(false);
    bridge.send({ type: 'prompt', request: { text, attachments, context } });
    ui.clearComposer();
  },
};

effect(() => {
  document.documentElement.dataset['scheme'] = store.scheme();
});

const root = document.getElementById('root');
if (!root) throw new Error('#root is missing');

const reviewable = __MOCK__ && native === null && window.innerWidth >= 760;
const host = reviewable ? devFrame(root, store) : root;

function start(): void {
  bridge.subscribe((event) => store.apply(event));
  mount(host, () => app(ctx));
  bridge.send({ type: 'ready' });
}

// The override exists because a false negative should not lock the user out of their own panel.
if (engineIsSupported()) start();
else renderUnsupported(host, start);
