// One interface, three transports: WebView2 on Windows, WKWebView on macOS, and an in-page mock so
// the panel runs (and is designed) in a plain browser.

import type { Cleanup } from '../core/signal.js';
import type { HostEvent, PanelCommand } from './events.js';

export interface Bridge {
  readonly kind: 'webview2' | 'wkwebview' | 'eto' | 'mock';
  send(command: PanelCommand): void;
  subscribe(handler: (event: HostEvent) => void): Cleanup;
}

interface WebView2Host {
  postMessage(message: string): void;
  addEventListener(type: 'message', handler: (event: { data: string }) => void): void;
  removeEventListener(type: 'message', handler: (event: { data: string }) => void): void;
}

interface WebKitHost {
  postMessage(message: unknown): void;
}

/** Eto injects this over the platform channel: webkit.messageHandlers on macOS, chrome.webview on Windows. */
interface EtoHost {
  postMessage(message: string): void;
}

declare global {
  interface Window {
    eto?: EtoHost;
    chrome?: { webview?: WebView2Host };
    webkit?: { messageHandlers?: Record<string, WebKitHost> };
    /** Both native hosts push events by calling this. */
    rhinoAI?: { receive(event: HostEvent): void };
  }
}

/** Native hosts deliver events by calling window.rhinoAI.receive; this owns that entry point. */
function nativeInbox(): (handler: (event: HostEvent) => void) => Cleanup {
  const handlers = new Set<(event: HostEvent) => void>();
  window.rhinoAI = {
    receive(event) {
      for (const handler of handlers) handler(event);
    },
  };
  return (handler) => {
    handlers.add(handler);
    return () => handlers.delete(handler);
  };
}

function webView2(host: WebView2Host): Bridge {
  const subscribe = nativeInbox();
  // WebView2 also delivers via its own message channel; accept both so the host can pick either.
  const onMessage = (event: { data: string }) => {
    try {
      window.rhinoAI?.receive(JSON.parse(event.data) as HostEvent);
    } catch {
      /* a malformed frame is dropped, never thrown into the render path */
    }
  };
  host.addEventListener('message', onMessage);

  return {
    kind: 'webview2',
    send: (command) => host.postMessage(JSON.stringify(command)),
    subscribe,
  };
}

function eto(host: EtoHost): Bridge {
  const subscribe = nativeInbox();
  return {
    kind: 'eto',
    send: (command) => host.postMessage(JSON.stringify(command)),
    subscribe,
  };
}

function webKit(host: WebKitHost): Bridge {
  const subscribe = nativeInbox();
  return {
    kind: 'wkwebview',
    send: (command) => host.postMessage(JSON.stringify(command)),
    subscribe,
  };
}

/**
 * Stands in when the host failed to inject its bridge. Says so rather than rendering an empty panel
 * that looks like a missing agent.
 */
export function inertBridge(): Bridge {
  return {
    kind: 'mock',
    send: () => {},
    subscribe: (handler) => {
      queueMicrotask(() =>
        handler({ type: 'notice', level: 'error', text: 'This panel could not reach Rhino.' }),
      );
      return () => {};
    },
  };
}

/**
 * null when no native host is present, so the caller can decide to run the mock.
 *
 * Order matters. Eto's WKWebView injects window.eto at document start, so on macOS it is already
 * there. Its WebView2 handler injects the same shim on DOMContentLoaded, which is *after* this
 * runs, but window.chrome.webview exists from the start on Windows and Eto listens to it, so the
 * WebView2 branch covers that case. Both end up in Eto's MessageReceived.
 */
export function resolveNativeBridge(): Bridge | null {
  const etoHost = window.eto;
  if (etoHost) return eto(etoHost);

  const edge = window.chrome?.webview;
  if (edge) return webView2(edge);

  const webkitHost = window.webkit?.messageHandlers?.['rhinoAI'];
  if (webkitHost) return webKit(webkitHost);

  return null;
}
