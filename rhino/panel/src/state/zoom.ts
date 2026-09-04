// Panel zoom, on the same discrete ladder a browser uses.
//
// CSS `zoom` rather than a transform, because zoom reflows: at 150% a docked panel really is
// narrower in CSS pixels, so the container queries fire and the layout adapts instead of the whole
// thing being scaled up and clipped.

import { signal, type ReadSignal } from '../core/signal.js';
import { DEFAULT, STEPS, indexOf, stepIn, stepOut, toCssZoom } from './zoomSteps.js';

const STORAGE_KEY = 'rhino-ai.zoom';

// LoadHtml gives the document an opaque origin, where touching localStorage throws rather than
// returning null, so both directions are guarded and zoom simply does not persist in that case.
function restore(): number {
  try {
    const stored = Number(window.localStorage.getItem(STORAGE_KEY));
    return Number.isFinite(stored) && stored > 0 ? (STEPS[indexOf(stored)] as number) : DEFAULT;
  } catch {
    return DEFAULT;
  }
}

export class Zoom {
  private readonly level = signal(restore());

  readonly value: ReadSignal<number> = this.level;

  /** The element the zoom applies to; set once the panel root exists. */
  private target: HTMLElement | null = null;

  attach(target: HTMLElement): void {
    this.target = target;
    this.apply();
  }

  set(value: number): void {
    this.level.set(value);
    this.apply();
    try {
      window.localStorage.setItem(STORAGE_KEY, String(value));
    } catch {
      /* opaque origin: the level still applies, it just will not survive a reload */
    }
  }

  in(): void {
    this.set(stepIn(this.level.peek()));
  }

  out(): void {
    this.set(stepOut(this.level.peek()));
  }

  reset(): void {
    this.set(DEFAULT);
  }

  private apply(): void {
    if (this.target) this.target.style.zoom = String(toCssZoom(this.level.peek()));
  }
}
