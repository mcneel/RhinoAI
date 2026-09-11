// Shown instead of the panel when the webview's engine is too old for the layout to hold together.
//
// WKWebView is the OS's WebKit, so on macOS this tracks the system Safari version rather than
// anything Rhino ships. Container queries are the probe because they are the newest thing the panel
// genuinely depends on: without them the narrow-panel rules never apply and a docked panel renders
// at the wide layout, which reads as broken rather than as degraded.
//
// Deliberately styled with primitives only. Nothing here may use a feature the check just failed.

import { el, mount } from '../core/dom.js';
import { t } from '../i18n/t.js';

const PROBE = '(container-type: inline-size)';

export function engineIsSupported(): boolean {
  return typeof CSS !== 'undefined' && typeof CSS.supports === 'function' && CSS.supports(PROBE);
}

export function renderUnsupported(root: HTMLElement, onOverride: () => void): void {
  mount(root, () =>
    el(
      'div',
      { class: 'unsupported' },
      el('h1', { text: () => t('unsupported.title') }),
      el('p', { text: () => t('unsupported.body') }),
      el('p', { text: () => t('unsupported.macos') }),
      el('p', { class: 'quiet', text: () => t('unsupported.classic') }),
      el(
        'button',
        {
          class: 'btn',
          type: 'button',
          onClick: () => {
            root.replaceChildren();
            onOverride();
          },
        },
        () => t('unsupported.showAnyway'),
      ),
    ),
  );
}
