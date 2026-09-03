// Right-click is handed to the host, which shows a real Rhino menu.
//
// An in-page menu was the wrong call: it cannot cascade like a native one, it does not match the
// rest of Rhino, and a `position: fixed` element inside the zoomed panel lands in the wrong place,
// because CSS zoom scales its coordinate space while clientX/clientY stay in viewport pixels.
//
// Text fields are left alone either way: the native field menu is the only route to Paste.

import { onCleanup } from '../core/dom.js';
import { DEFAULT, asPercent, canZoomIn, canZoomOut } from '../state/zoomSteps.js';
import type { PanelContext } from './context.js';

export function hostMenu(ctx: PanelContext): void {
  const onContextMenu = (event: MouseEvent) => {
    // With no host there is nothing to show a menu, so let the browser keep its own. That is the
    // prototype-in-a-browser case; inside Rhino the host always answers.
    if (!ctx.native) return;
    // The header is chrome: its buttons already do what they do, and a zoom menu over them is
    // noise. Text fields keep the native field menu, which is the only route to Paste.
    if (
      event.target instanceof Element &&
      event.target.closest('.header, textarea, input, [contenteditable="true"]')
    )
      return;

    event.preventDefault();
    const zoom = ctx.zoom.value();

    // clientX/clientY are viewport pixels, which is what the host needs to place the menu over the
    // cursor. Deliberately not divided by the zoom.
    ctx.send({
      type: 'menu.open',
      x: event.clientX,
      y: event.clientY,
      canZoomIn: canZoomIn(zoom),
      canZoomOut: canZoomOut(zoom),
      canResetZoom: zoom !== DEFAULT,
      zoomLabel: asPercent(zoom),
      selection: window.getSelection()?.toString() ?? '',
    });
  };

  window.addEventListener('contextmenu', onContextMenu);
  onCleanup(() => window.removeEventListener('contextmenu', onContextMenu));
}
