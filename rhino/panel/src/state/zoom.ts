// Panel zoom, on the same discrete ladder a browser uses.
//
// CSS `zoom` rather than a transform, because zoom reflows: at 150% a docked panel really is
// narrower in CSS pixels, so the container queries fire and the layout adapts instead of the whole
// thing being scaled up and clipped.

import { signal, type ReadSignal } from '../core/signal.js';
import { DEFAULT, STEPS, indexOf, stepIn, stepOut, toCssZoom } from './zoomSteps.js';

export class Zoom {
  private readonly level = signal(DEFAULT);
  private readonly persist: (level: number) => void;

  readonly value: ReadSignal<number> = this.level;

  /** The element the zoom applies to; set once the panel root exists. */
  private target: HTMLElement | null = null;

  constructor(persist: (level: number) => void) {
    this.persist = persist;
  }

  attach(target: HTMLElement): void {
    this.target = target;
    this.apply();
  }

  set(value: number): void {
    if (value === this.level.peek()) return;
    this.level.set(value);
    this.apply();
    this.persist(value);
  }

  /** The level the host had stored: applied, never handed straight back to it as a fresh choice. */
  restore(value: number): void {
    this.level.set(Number.isFinite(value) && value > 0 ? (STEPS[indexOf(value)] as number) : DEFAULT);
    this.apply();
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
